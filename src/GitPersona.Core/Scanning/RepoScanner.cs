using GitPersona.Core.Git;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.Scanning;

/// <summary>Finds working copies under some folders and reads what git says about each.</summary>
public sealed class RepoScanner(GitService git, ILogger<RepoScanner> logger)
{
    public const int DefaultMaxDepth = 5;

    private const int ParallelReads = 4;

    // Folders that hold dependencies or build output: big, and never the root of a repository the user cares about.
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", ".vs", ".idea", "packages", ".nuget", ".gradle",
        "target", "dist", "build", "out", ".venv", "venv", "__pycache__", "vendor",
    };

    private static readonly EnumerationOptions Children = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        // Junctions and symlinks can loop; hidden and system folders are not where people keep repositories.
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
    };

    /// <summary>
    /// Working copies under <paramref name="roots"/>, at most <paramref name="maxDepth"/> levels down.
    /// A repository's own subfolders are not searched (nested repositories and submodules are skipped).
    /// </summary>
    public static IEnumerable<string> FindRepositories(IEnumerable<string> roots, int maxDepth = DefaultMaxDepth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists).Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r))))
        {
            var pending = new Queue<(string Path, int Depth)>();
            pending.Enqueue((root, 0));
            while (pending.TryDequeue(out var current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(current.Path))
                {
                    continue;
                }

                if (IsRepository(current.Path))
                {
                    yield return current.Path;
                    continue;
                }

                if (current.Depth >= maxDepth)
                {
                    continue;
                }

                IEnumerable<string> children;
                try
                {
                    children = Directory.EnumerateDirectories(current.Path, "*", Children).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var child in children.Where(c => !SkippedFolders.Contains(Path.GetFileName(c))).Order(StringComparer.OrdinalIgnoreCase))
                {
                    pending.Enqueue((child, current.Depth + 1));
                }
            }
        }
    }

    public static bool IsRepository(string folder) =>
        Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git"));

    /// <summary>One git call per repository: remotes, every user.email in order (last one wins) and core.hooksPath.</summary>
    public async Task<RepoFacts> ReadFactsAsync(string repository, CancellationToken cancellationToken = default)
    {
        var result = await git.RunAsync(
            ["config", "--show-scope", "--get-regexp", @"^(remote\..*\.url|user\.email|core\.hookspath)$"],
            repository,
            timeout: TimeSpan.FromSeconds(15),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Exit code 1 only means "no matching keys".
        if (result.ExitCode is not (0 or 1))
        {
            var message = result.StandardError.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? $"git exited with {result.ExitCode}";
            return new RepoFacts(repository, [], null, null, null, message);
        }

        var remotes = new List<RemoteInfo>();
        string? effectiveEmail = null, localEmail = null, localHooksPath = null;
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<scope>\t<key> <value>"
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (tab < 0 || space < tab)
            {
                continue;
            }

            var scope = line[..tab];
            var key = line[(tab + 1)..space];
            var value = line[(space + 1)..].TrimEnd('\r');
            var isLocal = scope is "local" or "worktree";

            if (key == "user.email")
            {
                effectiveEmail = value;
                if (isLocal)
                {
                    localEmail = value;
                }
            }
            else if (key == "core.hookspath")
            {
                if (isLocal)
                {
                    localHooksPath = value;
                }
            }
            else if (key.StartsWith("remote.", StringComparison.Ordinal) && key.EndsWith(".url", StringComparison.Ordinal))
            {
                var name = key["remote.".Length..^".url".Length];
                if (remotes.All(r => r.Name != name))
                {
                    remotes.Add(new RemoteInfo(name, value));
                }
            }
        }

        return new RepoFacts(repository, remotes, effectiveEmail, localEmail, localHooksPath);
    }

    /// <summary>Finds repositories and reports each one's facts as soon as they are read.</summary>
    public async Task ScanAsync(IReadOnlyList<string> roots, int maxDepth, IProgress<RepoFacts> found, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(found);
        var repositories = await Task.Run(() => FindRepositories(roots, maxDepth, cancellationToken).ToList(), cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Found {Count} repositories under {Roots}", repositories.Count, string.Join("; ", roots));

        using var gate = new SemaphoreSlim(ParallelReads);
        var reads = repositories.Select(async repository =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                found.Report(await ReadFactsAsync(repository, cancellationToken).ConfigureAwait(false));
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(reads).ConfigureAwait(false);
    }
}
