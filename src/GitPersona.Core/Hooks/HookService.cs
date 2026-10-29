using System.Globalization;
using System.Text;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Models;
using GitPersona.Core.Profiles;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.Hooks;

/// <summary>Localized texts the pre-commit check prints; the shell script has no translations of its own.</summary>
public sealed record HookMessages(
    string Blocked,
    string Profile,
    string Expected,
    string Current,
    string NotSet,
    string Fix,
    string Bypass,
    string Conflict);

/// <summary>
/// Maintains GitPersona's global hooks folder (<c>%APPDATA%\GitPersona\hooks</c>): a stub for every git hook,
/// the dispatcher script, its settings and the identity rules derived from the profiles.
/// Everything in it belongs to GitPersona, so it is written directly (no review needed);
/// only <c>core.hooksPath</c> in <c>~/.gitconfig</c> goes through the reviewed config plan.
/// </summary>
public sealed class HookService(AppPaths paths, ILogger<HookService> logger)
{
    public const string Marker = "# Managed by GitPersona";

    /// <summary>
    /// The client-side hooks that get a stub, i.e. that keep working in repositories while the guard is on.
    /// Every stub costs a shell start each time git runs that hook, so hooks git fires very often
    /// (reference-transaction, post-index-change), server-side hooks and p4 hooks are left out.
    /// </summary>
    public static IReadOnlyList<string> HookNames { get; } =
    [
        "pre-commit", "prepare-commit-msg", "commit-msg", "post-commit", "pre-merge-commit",
        "pre-push", "pre-rebase", "post-checkout", "post-merge", "post-rewrite",
        "applypatch-msg", "pre-applypatch", "post-applypatch", "pre-auto-gc", "sendemail-validate",
    ];

    // Sourced rather than exec'd: one shell per hook instead of two.
    internal const string Stub =
        "#!/bin/sh\n" +
        Marker + ": forwards to the commit guard dispatcher.\n" +
        ". \"${0%/*}/_gitpersona/dispatch.sh\"\n";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly string[] InternalFiles = ["dispatch.sh", "guard.conf", "rules.tsv"];

    public string HooksDirectory => paths.HooksDirectory;

    private string InternalDirectory => Path.Combine(HooksDirectory, "_gitpersona");

    private string DispatchFile => Path.Combine(InternalDirectory, "dispatch.sh");

    public bool IsInstalled =>
        File.Exists(DispatchFile) && File.ReadAllText(DispatchFile).Contains(Marker, StringComparison.Ordinal);

    /// <param name="previousHooksPath">A global hooks folder the user had before; its hooks are chained.</param>
    /// <param name="guardEnabled">When <see langword="false"/> the stubs only chain the repositories' own hooks.</param>
    public void Install(IReadOnlyList<Profile> profiles, HookMessages messages, string? previousHooksPath, bool guardEnabled)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(messages);
        Directory.CreateDirectory(InternalDirectory);
        foreach (var name in HookNames)
        {
            WriteLf(Path.Combine(HooksDirectory, name), Stub);
        }

        WriteLf(DispatchFile, DispatchScript());
        WriteLf(Path.Combine(InternalDirectory, "guard.conf"), BuildSettings(messages, previousHooksPath, guardEnabled));
        WriteLf(Path.Combine(InternalDirectory, "rules.tsv"), BuildRules(profiles));
        logger.LogInformation("Commit guard hooks installed in {Directory} (guard enabled: {Enabled})", HooksDirectory, guardEnabled);
    }

    /// <summary>Deletes only files GitPersona wrote; anything else in the folder is left alone.</summary>
    public void Remove()
    {
        if (!Directory.Exists(HooksDirectory))
        {
            return;
        }

        foreach (var name in HookNames)
        {
            var file = Path.Combine(HooksDirectory, name);
            if (File.Exists(file) && File.ReadAllText(file) == Stub)
            {
                File.Delete(file);
            }
        }

        foreach (var file in InternalFiles.Select(f => Path.Combine(InternalDirectory, f)))
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        DeleteIfEmpty(InternalDirectory);
        DeleteIfEmpty(HooksDirectory);
        logger.LogInformation("Commit guard hooks removed from {Directory}", HooksDirectory);
    }

    /// <summary>
    /// <c>alias  &lt;alias&gt;  &lt;email&gt;  &lt;name&gt;</c> and <c>folder  &lt;path&gt;  &lt;email&gt;  &lt;name&gt;</c>, tab separated.
    /// Paths use forward slashes and ASCII lower case, the same normalisation the script applies to
    /// <c>git rev-parse --show-toplevel</c>.
    /// </summary>
    internal static string BuildRules(IReadOnlyList<Profile> profiles)
    {
        var builder = new StringBuilder();
        foreach (var profile in profiles)
        {
            var label = Clean($"{profile.DisplayName} ({profile.HostAlias})");
            var email = Clean(profile.GitUserEmail);
            if (!string.IsNullOrWhiteSpace(profile.HostAlias))
            {
                builder.Append(CultureInfo.InvariantCulture, $"alias\t{AsciiLower(profile.HostAlias.Trim())}\t{email}\t{label}\n");
            }

            foreach (var folder in profile.Folders.Where(Path.IsPathFullyQualified))
            {
                // Git Bash's `pwd -W` reports long names, so rules must not keep 8.3 short names.
                var normalized = AsciiLower(ProfileValidator.NormalizeFolder(LongPath.Expand(folder)).Replace('\\', '/'));
                builder.Append(CultureInfo.InvariantCulture, $"folder\t{Clean(normalized)}\t{email}\t{label}\n");
            }
        }

        return builder.ToString();
    }

    internal static string BuildSettings(HookMessages messages, string? previousHooksPath, bool guardEnabled)
    {
        var lines = new[]
        {
            Marker + ": settings for dispatch.sh, rewritten automatically.",
            $"GP_GUARD_ENABLED={ShellQuote(guardEnabled ? "1" : "0")}",
            $"GP_PREVIOUS_HOOKS_PATH={ShellQuote(previousHooksPath?.Replace('\\', '/') ?? string.Empty)}",
            $"GP_MSG_BLOCKED={ShellQuote(messages.Blocked)}",
            $"GP_MSG_PROFILE={ShellQuote(messages.Profile)}",
            $"GP_MSG_EXPECTED={ShellQuote(messages.Expected)}",
            $"GP_MSG_CURRENT={ShellQuote(messages.Current)}",
            $"GP_MSG_NOT_SET={ShellQuote(messages.NotSet)}",
            $"GP_MSG_FIX={ShellQuote(messages.Fix)}",
            $"GP_MSG_BYPASS={ShellQuote(messages.Bypass)}",
            $"GP_MSG_CONFLICT={ShellQuote(messages.Conflict)}",
        };
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>POSIX single quoting: <c>it's</c> → <c>'it'\''s'</c>.</summary>
    internal static string ShellQuote(string value) => "'" + Clean(value).Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string DispatchScript()
    {
        using var stream = typeof(HookService).Assembly.GetManifestResourceStream("GitPersona.Core.Hooks.dispatch.sh")
            ?? throw new InvalidOperationException("The dispatch.sh resource is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    // Tabs and line breaks would break the rules file and the settings lines.
    private static string Clean(string text) => string.Concat(text.Select(c => char.IsControl(c) ? ' ' : c));

    private static string AsciiLower(string text) => string.Concat(text.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c));

    private static void WriteLf(string path, string content) => AtomicFileWriter.Write(path, Utf8NoBom.GetBytes(content));

    private static void DeleteIfEmpty(string directory)
    {
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }
}
