using System.ComponentModel;
using GitPersona.Core.Git;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Models;
using GitPersona.Core.Security;
using GitPersona.Core.Ssh;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.Diagnostics;

public enum DiagnosticSeverity
{
    Ok,
    Info,
    Warning,
    Error,
}

public enum DiagnosticCode
{
    GitMissing,
    GitVersion,
    SshUsedByGit,
    HomeVariable,
    SshVariable,
    KnownHostsMissing,
    KnownHostsCurrent,
    KnownHostsMismatch,
    AgentNotNeeded,
    AgentUnavailable,
    AgentKeyNotLoaded,
    AgentReady,
    BundledSshIgnoresAgent,
    KeyMissing,
    KeyPermissions,
    StaleCredential,
}

public enum DiagnosticActionKind
{
    InstallGitHubHostKeys,
    RestrictKeyFile,
    DeleteCredential,
    UseWindowsOpenSsh,

    /// <summary>A command the user runs themselves (e.g. as administrator); GitPersona only copies it.</summary>
    CopyCommand,
}

public sealed record DiagnosticAction(DiagnosticActionKind Kind, string? Argument = null);

/// <param name="Subject">What the item is about (profile, file, credential target).</param>
/// <param name="Detail">Facts to show, e.g. a version or a list of accounts.</param>
/// <param name="Variant">A sub-case the UI words differently, e.g. where git's ssh comes from.</param>
public sealed record DiagnosticItem(
    DiagnosticCode Code,
    DiagnosticSeverity Severity,
    string? Subject = null,
    string? Detail = null,
    DiagnosticAction? Action = null,
    string? Variant = null);

/// <summary>Windows-specific checks of the git/ssh setup. Read-only: fixes are separate, confirmed actions.</summary>
public sealed class DiagnosticsService(
    GitService git,
    ToolLocator tools,
    IProcessRunner runner,
    GitSshResolver sshResolver,
    KnownHostsService knownHosts,
    SshAgentProbe agent,
    SshKeyService keys,
    CredentialStore credentials,
    UserPaths paths,
    ILogger<DiagnosticsService> logger)
{
    public async Task<IReadOnlyList<DiagnosticItem>> RunAsync(IReadOnlyList<Profile> profiles, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var items = new List<DiagnosticItem>();

        if (tools.Git is null)
        {
            items.Add(new DiagnosticItem(DiagnosticCode.GitMissing, DiagnosticSeverity.Error));
            return items;
        }

        var version = await git.RunAsync(["--version"], cancellationToken: cancellationToken).ConfigureAwait(false);
        items.Add(new DiagnosticItem(DiagnosticCode.GitVersion, DiagnosticSeverity.Ok, Detail: version.StandardOutput.Trim()));

        var ssh = await sshResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (ssh is not null)
        {
            var sshVersion = await SshVersionAsync(ssh.FileName, cancellationToken).ConfigureAwait(false);
            items.Add(new DiagnosticItem(DiagnosticCode.SshUsedByGit, DiagnosticSeverity.Info, ssh.FileName, sshVersion, Variant: ssh.Source.ToString()));
        }

        AddEnvironmentChecks(items);
        await AddKnownHostsCheckAsync(items, profiles, cancellationToken).ConfigureAwait(false);
        await AddKeyAndAgentChecksAsync(items, profiles, ssh, cancellationToken).ConfigureAwait(false);
        AddCredentialChecks(items);
        return items;
    }

    private void AddEnvironmentChecks(List<DiagnosticItem> items)
    {
        // Git for Windows reads ~/.gitconfig and its ssh reads ~/.ssh from HOME; Windows OpenSSH always uses USERPROFILE.
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home) && !SamePath(home, paths.HomeDirectory))
        {
            items.Add(new DiagnosticItem(DiagnosticCode.HomeVariable, DiagnosticSeverity.Warning, home, paths.HomeDirectory));
        }

        foreach (var name in new[] { "GIT_SSH_COMMAND", "GIT_SSH" })
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                items.Add(new DiagnosticItem(DiagnosticCode.SshVariable, DiagnosticSeverity.Warning, name, value));
            }
        }
    }

    private async Task AddKnownHostsCheckAsync(List<DiagnosticItem> items, IReadOnlyList<Profile> profiles, CancellationToken cancellationToken)
    {
        if (profiles.Count > 0 && !profiles.Any(p => p.Host.Equals(KnownHostsService.GitHubHost, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var report = await knownHosts.CheckGitHubAsync(cancellationToken).ConfigureAwait(false);
        var fix = new DiagnosticAction(DiagnosticActionKind.InstallGitHubHostKeys);
        items.Add(report.State switch
        {
            KnownHostsState.Missing => new DiagnosticItem(DiagnosticCode.KnownHostsMissing, DiagnosticSeverity.Warning, paths.KnownHostsFile, Action: fix),
            KnownHostsState.Mismatch => new DiagnosticItem(DiagnosticCode.KnownHostsMismatch, DiagnosticSeverity.Error, paths.KnownHostsFile, string.Join(", ", report.UnknownFingerprints), fix),
            _ => new DiagnosticItem(DiagnosticCode.KnownHostsCurrent, DiagnosticSeverity.Ok, paths.KnownHostsFile),
        });
    }

    private async Task AddKeyAndAgentChecksAsync(List<DiagnosticItem> items, IReadOnlyList<Profile> profiles, SshCommand? ssh, CancellationToken cancellationToken)
    {
        var protectedKeys = new List<(Profile Profile, string? Fingerprint)>();
        foreach (var profile in profiles.Where(p => !string.IsNullOrWhiteSpace(p.SshKeyPath)))
        {
            if (!File.Exists(profile.SshKeyPath))
            {
                items.Add(new DiagnosticItem(DiagnosticCode.KeyMissing, DiagnosticSeverity.Error, profile.DisplayName, profile.SshKeyPath));
                continue;
            }

            var loose = KeyFilePermissions.LooseAccess(profile.SshKeyPath);
            if (loose.Count > 0)
            {
                items.Add(new DiagnosticItem(DiagnosticCode.KeyPermissions, DiagnosticSeverity.Warning, profile.SshKeyPath, string.Join(", ", loose),
                    new DiagnosticAction(DiagnosticActionKind.RestrictKeyFile, profile.SshKeyPath)));
            }

            try
            {
                if (await keys.HasPassphraseAsync(profile.SshKeyPath, cancellationToken).ConfigureAwait(false))
                {
                    var info = await keys.DescribeAsync(profile.SshKeyPath, cancellationToken).ConfigureAwait(false);
                    protectedKeys.Add((profile, info.Fingerprint));
                }
            }
            catch (Exception ex) when (ex is SshKeyException or ProcessRunException or ToolNotFoundException)
            {
                logger.LogWarning(ex, "Could not inspect {Key}", profile.SshKeyPath);
            }
        }

        if (protectedKeys.Count == 0)
        {
            items.Add(new DiagnosticItem(DiagnosticCode.AgentNotNeeded, DiagnosticSeverity.Ok));
            return;
        }

        var status = await agent.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!status.Running)
        {
            const string enable = "Get-Service ssh-agent | Set-Service -StartupType Automatic; Start-Service ssh-agent";
            items.Add(new DiagnosticItem(DiagnosticCode.AgentUnavailable, DiagnosticSeverity.Warning, status.StartMode.ToString(), enable,
                new DiagnosticAction(DiagnosticActionKind.CopyCommand, enable)));
        }
        else
        {
            var missing = protectedKeys.Where(k => k.Fingerprint is null || !status.LoadedFingerprints.Contains(k.Fingerprint)).ToList();
            foreach (var (profile, _) in missing)
            {
                var add = $"ssh-add \"{profile.SshKeyPath}\"";
                items.Add(new DiagnosticItem(DiagnosticCode.AgentKeyNotLoaded, DiagnosticSeverity.Warning, profile.DisplayName, add,
                    new DiagnosticAction(DiagnosticActionKind.CopyCommand, add)));
            }

            if (missing.Count == 0)
            {
                items.Add(new DiagnosticItem(DiagnosticCode.AgentReady, DiagnosticSeverity.Ok));
            }
        }

        // Git's bundled (MSYS) ssh cannot talk to the Windows agent's named pipe.
        if (ssh is { IsWindowsOpenSsh: false })
        {
            items.Add(new DiagnosticItem(DiagnosticCode.BundledSshIgnoresAgent, DiagnosticSeverity.Warning, ssh.FileName,
                Action: new DiagnosticAction(DiagnosticActionKind.UseWindowsOpenSsh)));
        }
    }

    /// <summary>Git Credential Manager's saved github.com logins; they are offered for deletion, never deleted automatically.</summary>
    private void AddCredentialChecks(List<DiagnosticItem> items)
    {
        try
        {
            foreach (var entry in credentials.Enumerate("git:https://*").Where(e => e.TargetName.Contains("github", StringComparison.OrdinalIgnoreCase)))
            {
                items.Add(new DiagnosticItem(DiagnosticCode.StaleCredential, DiagnosticSeverity.Info, entry.TargetName, entry.UserName,
                    new DiagnosticAction(DiagnosticActionKind.DeleteCredential, entry.TargetName)));
            }
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "Could not list Credential Manager entries");
        }
    }

    private async Task<string> SshVersionAsync(string ssh, CancellationToken cancellationToken)
    {
        try
        {
            var result = await runner.RunAsync(new ProcessRequest(ssh, ["-V"]) { Timeout = TimeSpan.FromSeconds(10) }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return (result.StandardError + result.StandardOutput).Split(',')[0].Trim();
        }
        catch (ProcessRunException)
        {
            return "?";
        }
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
