using System.IO;
using GitPersona.App.Localization;
using GitPersona.Core.Git;
using GitPersona.Core.Hooks;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Models;
using Microsoft.Extensions.Logging;

namespace GitPersona.App.Services;

/// <summary>
/// Keeps the hooks folder in step with the profiles, the language and the guard setting.
/// While <c>~/.gitconfig</c> still points core.hooksPath at the folder, the stubs stay installed
/// (with the check off) so repositories never lose their own hooks; once it no longer does, they are removed.
/// </summary>
public sealed class CommitGuardService(
    HookService hooks,
    SettingsService settings,
    ProfilesService profiles,
    ConfigReviewService review,
    GitService git,
    UserPaths userPaths,
    ILogger<CommitGuardService> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static LocalizationManager Loc => LocalizationManager.Instance;

    public void Initialize()
    {
        profiles.Changed += OnChanged;
        settings.Changed += OnSettingsChanged;
        review.Applied += OnChanged;
        Loc.LanguageChanged += OnChanged;
        _ = SyncAsync();
    }

    public async Task SyncAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var configured = await ReadGlobalHooksPathsAsync();
            var ours = Normalize(hooks.HooksDirectory);
            var referenced = configured.Any(path => Normalize(path) == ours);
            var previous = configured.LastOrDefault(path => Normalize(path) != ours);
            var enabled = settings.Current.CommitGuardEnabled;

            if (enabled || referenced)
            {
                hooks.Install(profiles.Profiles, Messages(), previous, enabled);
            }
            else if (hooks.IsInstalled)
            {
                hooks.Remove();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProcessRunException or ToolNotFoundException)
        {
            logger.LogError(ex, "Could not update the commit guard hooks");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        profiles.Changed -= OnChanged;
        settings.Changed -= OnSettingsChanged;
        review.Applied -= OnChanged;
        Loc.LanguageChanged -= OnChanged;
        _gate.Dispose();
    }

    private void OnChanged(object? sender, EventArgs e) => _ = SyncAsync();

    private void OnSettingsChanged(object? sender, AppSettings e) => _ = SyncAsync();

    /// <summary>core.hooksPath values in the managed ~/.gitconfig, in file order (GitPersona's block is last).</summary>
    private async Task<IReadOnlyList<string>> ReadGlobalHooksPathsAsync()
    {
        if (!File.Exists(userPaths.GlobalGitConfigFile))
        {
            return [];
        }

        var result = await git.RunAsync(["config", "--file", userPaths.GlobalGitConfigFile, "--get-all", "core.hooksPath"]);
        return result.StandardOutput
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ExpandHome)
            .ToList();
    }

    private string ExpandHome(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(userPaths.HomeDirectory, path[2..]) : path;

    private static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Replace('\\', '/').ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    /// <summary>Labels are padded to one width so the hook's output lines up in every language.</summary>
    private static HookMessages Messages()
    {
        string[] labels = [Loc["Guard_Profile"], Loc["Guard_Expected"], Loc["Guard_Current"]];
        var width = labels.Max(label => label.Length);
        return new HookMessages(
            Loc["Guard_Blocked"],
            labels[0].PadRight(width),
            labels[1].PadRight(width),
            labels[2].PadRight(width),
            Loc["Guard_NotSet"],
            Loc["Guard_Fix"],
            Loc["Guard_Bypass"],
            Loc["Guard_Conflict"]);
    }
}
