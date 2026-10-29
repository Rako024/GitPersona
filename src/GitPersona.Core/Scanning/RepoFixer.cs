using GitPersona.Core.Git;
using GitPersona.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.Scanning;

public sealed class RepoFixException(string message) : Exception(message);

/// <summary>Applies a <see cref="RepoFix"/> through git, after backing up the repository's config file.</summary>
public sealed class RepoFixer(GitService git, BackupManager backups, ILogger<RepoFixer> logger)
{
    /// <returns>The backup of the repository's config file.</returns>
    /// <exception cref="RepoFixException">git refused the change.</exception>
    public async Task<string?> ApplyAsync(string repository, RepoFix fix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fix);
        string[] arguments = fix.Kind switch
        {
            RepoFixKind.SetRemoteUrl => ["remote", "set-url", fix.Remote ?? throw new ArgumentException("A remote name is required.", nameof(fix)), fix.NewUrl ?? throw new ArgumentException("A URL is required.", nameof(fix))],
            RepoFixKind.UnsetLocalEmail => ["config", "--local", "--unset-all", "user.email"],
            _ => throw new ArgumentOutOfRangeException(nameof(fix)),
        };

        var backup = backups.Backup(await ConfigFileAsync(repository, cancellationToken).ConfigureAwait(false));
        var result = await git.RunAsync(arguments, repository, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new RepoFixException(result.StandardError.Trim() is { Length: > 0 } error ? error : $"git exited with {result.ExitCode}");
        }

        logger.LogInformation("Fixed {Repository}: {Fix} (backup: {Backup})", repository, fix, backup ?? "none");
        return backup;
    }

    /// <summary>The config file git writes for this repository; worktrees share the main repository's.</summary>
    private async Task<string> ConfigFileAsync(string repository, CancellationToken cancellationToken)
    {
        var result = await git.RunAsync(["rev-parse", "--git-common-dir"], repository, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new RepoFixException(result.StandardError.Trim());
        }

        var commonDirectory = result.StandardOutput.Trim();
        return Path.Combine(Path.GetFullPath(Path.Combine(repository, commonDirectory)), "config");
    }
}
