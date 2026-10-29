using System.Globalization;
using System.Text.RegularExpressions;
using GitPersona.Core.Models;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.Git;

public enum ClonePhase
{
    Starting,
    Counting,
    Compressing,
    Receiving,
    Resolving,
    Updating,
}

/// <param name="Overall">0–100 across all phases, for a single progress bar.</param>
public sealed record CloneProgress(ClonePhase Phase, int Percent, double Overall);

public enum CloneFailure
{
    /// <summary>ssh does not know (or distrusts) the server's host key.</summary>
    HostKeyVerification,

    /// <summary>The server rejected the key: it is not on any account.</summary>
    PermissionDenied,

    /// <summary>GitHub's answer when the key's account has no access — the classic "wrong account" symptom.</summary>
    RepositoryNotFound,

    /// <summary>The SSH alias is not defined (configuration not applied).</summary>
    HostNotResolved,

    TargetNotEmpty,
    Other,
}

public sealed class CloneFailedException(CloneFailure failure, string message) : Exception(message)
{
    public CloneFailure Failure { get; } = failure;
}

/// <summary>Clones through the profile's SSH alias and reports git's progress.</summary>
public sealed partial class CloneService(GitService git, ILogger<CloneService> logger)
{
    // "Receiving objects:  45% (450/1000), 1.20 MiB | 2.30 MiB/s" — optionally prefixed with "remote: ".
    [GeneratedRegex(@"^(?:remote:\s*)?(?<phase>Counting objects|Compressing objects|Receiving objects|Resolving deltas|Updating files):\s+(?<percent>\d{1,3})%", RegexOptions.CultureInvariant)]
    private static partial Regex ProgressLine();

    public static string RemoteUrlFor(Profile profile, RepoRef repository)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(repository);
        return $"git@{profile.HostAlias}:{repository.Owner}/{repository.Name}.git";
    }

    /// <summary><c>&lt;default clone folder or first profile folder&gt;\&lt;repo&gt;</c>, or <see langword="null"/> when the profile has no folder.</summary>
    public static string? DefaultTargetFor(Profile profile, RepoRef repository)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(repository);
        var root = !string.IsNullOrWhiteSpace(profile.DefaultCloneFolder) ? profile.DefaultCloneFolder
            : profile.Folders.Count > 0 ? profile.Folders[0]
            : null;
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, repository.Name);
    }

    public static CloneProgress? ParseProgress(string line)
    {
        var match = ProgressLine().Match(line ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        var percent = Math.Clamp(int.Parse(match.Groups["percent"].Value, CultureInfo.InvariantCulture), 0, 100);
        var (phase, start, weight) = match.Groups["phase"].Value switch
        {
            "Counting objects" => (ClonePhase.Counting, 0, 5),
            "Compressing objects" => (ClonePhase.Compressing, 5, 5),
            "Receiving objects" => (ClonePhase.Receiving, 10, 70),
            "Resolving deltas" => (ClonePhase.Resolving, 80, 15),
            _ => (ClonePhase.Updating, 95, 5),
        };
        return new CloneProgress(phase, percent, start + (weight * percent / 100.0));
    }

    public static CloneFailure Classify(string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardError);
        bool Has(string text) => standardError.Contains(text, StringComparison.OrdinalIgnoreCase);
        if (Has("Host key verification failed") || Has("REMOTE HOST IDENTIFICATION HAS CHANGED"))
        {
            return CloneFailure.HostKeyVerification;
        }

        if (Has("Could not resolve hostname"))
        {
            return CloneFailure.HostNotResolved;
        }

        if (Has("Permission denied (publickey"))
        {
            return CloneFailure.PermissionDenied;
        }

        return Has("Repository not found") ? CloneFailure.RepositoryNotFound : CloneFailure.Other;
    }

    /// <param name="output">Non-progress lines from git (e.g. "Cloning into ..."), for a log view.</param>
    /// <exception cref="CloneFailedException">git failed; a folder created for the clone is removed again.</exception>
    /// <exception cref="OperationCanceledException">The clone was stopped; a folder created for it is removed.</exception>
    public async Task CloneAsync(
        string remoteUrl,
        string targetDirectory,
        IProgress<CloneProgress>? progress = null,
        IProgress<string>? output = null,
        CancellationToken cancellationToken = default)
    {
        var target = Path.GetFullPath(targetDirectory);
        var existed = Directory.Exists(target);
        if (existed && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new CloneFailedException(CloneFailure.TargetNotEmpty, $"'{target}' already exists and is not empty.");
        }

        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        progress?.Report(new CloneProgress(ClonePhase.Starting, 0, 0));
        var lines = new LineRouter(progress, output);

        try
        {
            var result = await git.RunAsync(
                ["clone", "--progress", remoteUrl, target],
                workingDirectory: parent,
                timeout: Timeout.InfiniteTimeSpan,
                errorLines: lines,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                var failure = Classify(result.StandardError);
                logger.LogWarning("Clone of {Url} failed ({Failure}): {Error}", remoteUrl, failure, result.StandardError.Trim());
                RemoveIfCreated(target, existed);
                throw new CloneFailedException(failure, MeaningfulLine(result.StandardError));
            }
        }
        catch (OperationCanceledException)
        {
            RemoveIfCreated(target, existed);
            throw;
        }

        logger.LogInformation("Cloned {Url} into {Target}", remoteUrl, target);
    }

    private void RemoveIfCreated(string target, bool existed)
    {
        if (existed || !Directory.Exists(target))
        {
            return;
        }

        try
        {
            // Only the folder this clone created; git may leave it half-written when stopped.
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal); // pack files are read-only
            }

            Directory.Delete(target, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove the incomplete clone at {Target}", target);
        }
    }

    /// <summary>
    /// The line that explains the failure. git ends with generic advice ("Please make sure you have the correct
    /// access rights / and the repository exists."), so the first error line is the informative one.
    /// </summary>
    private static string MeaningfulLine(string standardError)
    {
        var lines = standardError.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith("Cloning into", StringComparison.Ordinal))
            .ToList();
        string[] prefixes = ["ERROR:", "fatal:", "error:", "ssh:"];
        return lines.FirstOrDefault(line => prefixes.Any(p => line.StartsWith(p, StringComparison.Ordinal)))
            ?? lines.FirstOrDefault()
            ?? "git clone failed.";
    }

    /// <summary>Splits git's stderr into progress updates and ordinary output lines.</summary>
    private sealed class LineRouter(IProgress<CloneProgress>? progress, IProgress<string>? output) : IProgress<string>
    {
        public void Report(string value)
        {
            if (ParseProgress(value) is { } parsed)
            {
                progress?.Report(parsed);
            }
            else if (!string.IsNullOrWhiteSpace(value))
            {
                output?.Report(value.Trim());
            }
        }
    }
}
