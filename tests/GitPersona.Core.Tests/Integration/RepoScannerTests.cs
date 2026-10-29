using GitPersona.Core.ConfigFiles;
using GitPersona.Core.Git;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Scanning;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.Integration;

/// <summary>Discovery, git facts and fixes on real repositories in a throwaway HOME.</summary>
public sealed class RepoScannerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly UserPaths _home;
    private readonly GitService _git;
    private readonly RepoScanner _scanner;

    public RepoScannerTests()
    {
        _home = new UserPaths(_temp.Combine("home"));
        _git = new GitService(new ProcessRunner(NullLogger<ProcessRunner>.Instance), new ToolLocator(), new Dictionary<string, string?>
        {
            ["HOME"] = _home.HomeDirectory,
            ["USERPROFILE"] = _home.HomeDirectory,
            ["XDG_CONFIG_HOME"] = _temp.Combine("home", ".config"),
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = null,
        });
        _scanner = new RepoScanner(_git, NullLogger<RepoScanner>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    private async Task<string> RepoAsync(string path, string? remote = null)
    {
        Directory.CreateDirectory(path);
        Assert.True((await _git.RunAsync(["init", "-q"], path)).Succeeded);
        if (remote is not null)
        {
            Assert.True((await _git.RunAsync(["remote", "add", "origin", remote], path)).Succeeded);
        }

        return path;
    }

    [Fact]
    public async Task Finds_repositories_without_descending_into_them_or_into_dependency_folders()
    {
        var root = _temp.Combine("code");
        var a = await RepoAsync(Path.Combine(root, "a"));
        await RepoAsync(Path.Combine(a, "nested"));
        await RepoAsync(Path.Combine(root, "node_modules", "pkg"));
        var deep = await RepoAsync(Path.Combine(root, "x", "y", "z"));
        await RepoAsync(Path.Combine(root, "1", "2", "3", "4", "too-deep"));

        var found = RepoScanner.FindRepositories([root, Path.Combine(root, "x")], maxDepth: 3).ToList();

        Assert.Equal([a, deep], found.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reads_remotes_the_effective_email_and_local_overrides()
    {
        var work = TestProfiles.Create("work", _home.HomeDirectory, _temp.Combine("work"));
        Directory.CreateDirectory(_home.SshDirectory);
        File.WriteAllText(work.SshKeyPath, "dummy");
        new ConfigWriter(new BackupManager(), NullLogger<ConfigWriter>.Instance).Apply(new ConfigPlanner(_home).Plan([work]).Changes);

        var clean = await RepoAsync(_temp.Combine("work", "clean"), "git@github-work:acme/clean.git");
        var overridden = await RepoAsync(_temp.Combine("work", "overridden"), "https://github.com/acme/overridden.git");
        await _git.RunAsync(["remote", "add", "upstream", "https://github.com/original/overridden.git"], overridden);
        await _git.RunAsync(["config", "user.email", "old@corp.example"], overridden);
        await _git.RunAsync(["config", "core.hooksPath", ".husky/_"], overridden);

        var cleanFacts = await _scanner.ReadFactsAsync(clean);
        var overriddenFacts = await _scanner.ReadFactsAsync(overridden);

        Assert.Equal("work@example.com", cleanFacts.EffectiveEmail);
        Assert.Null(cleanFacts.LocalEmail);
        Assert.Equal([new RemoteInfo("origin", "git@github-work:acme/clean.git")], cleanFacts.Remotes);
        Assert.Equal("old@corp.example", overriddenFacts.EffectiveEmail);
        Assert.Equal("old@corp.example", overriddenFacts.LocalEmail);
        Assert.Equal(".husky/_", overriddenFacts.LocalHooksPath);
        Assert.Equal(["origin", "upstream"], overriddenFacts.Remotes.Select(r => r.Name));

        var report = RepoAnalyzer.Analyze(overriddenFacts, [work], guardEnabled: true);
        Assert.Equal(
            [RepoIssueCode.HttpsRemote, RepoIssueCode.EmailMismatch, RepoIssueCode.OwnHooksPath],
            report.Issues.Select(i => i.Code));
    }

    [Fact]
    public async Task Fixes_change_the_repository_config_after_backing_it_up()
    {
        var repo = await RepoAsync(_temp.Combine("work", "api"), "https://github.com/acme/api.git");
        await _git.RunAsync(["config", "user.email", "old@corp.example"], repo);
        var fixer = new RepoFixer(_git, new BackupManager(), NullLogger<RepoFixer>.Instance);

        var backup = await fixer.ApplyAsync(repo, new RepoFix(RepoFixKind.SetRemoteUrl, "origin", "git@github-work:acme/api.git"));
        await fixer.ApplyAsync(repo, new RepoFix(RepoFixKind.UnsetLocalEmail));

        var facts = await _scanner.ReadFactsAsync(repo);
        Assert.Equal("git@github-work:acme/api.git", facts.Remotes.Single().Url);
        Assert.Null(facts.LocalEmail);
        Assert.NotNull(backup);
        Assert.Contains("https://github.com/acme/api.git", File.ReadAllText(backup), StringComparison.Ordinal);
        Assert.Equal(Path.Combine(repo, ".git"), Path.GetDirectoryName(backup));
    }

    [Fact]
    public async Task A_fix_git_refuses_is_reported()
    {
        var repo = await RepoAsync(_temp.Combine("work", "api"));
        var fixer = new RepoFixer(_git, new BackupManager(), NullLogger<RepoFixer>.Instance);

        await Assert.ThrowsAsync<RepoFixException>(() => fixer.ApplyAsync(repo, new RepoFix(RepoFixKind.SetRemoteUrl, "origin", "git@github-work:a/b.git")));
    }

    [Fact]
    public async Task Scanning_reports_every_repository()
    {
        var root = _temp.Combine("code");
        await RepoAsync(Path.Combine(root, "one"), "git@github.com:a/one.git");
        await RepoAsync(Path.Combine(root, "two"));
        var reported = new List<RepoFacts>();

        await _scanner.ScanAsync([root], RepoScanner.DefaultMaxDepth, new SynchronousProgress(reported));

        Assert.Equal(["one", "two"], reported.Select(f => Path.GetFileName(f.Path)).Order());
    }

    private sealed class SynchronousProgress(List<RepoFacts> sink) : IProgress<RepoFacts>
    {
        public void Report(RepoFacts value)
        {
            lock (sink)
            {
                sink.Add(value);
            }
        }
    }
}
