using GitPersona.Core.Git;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.Git;

public sealed class CloneServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly GitService _git;
    private readonly CloneService _clone;

    public CloneServiceTests()
    {
        // Isolated from the developer's git config (hooks, includes, credential helpers).
        var isolated = new Dictionary<string, string?>
        {
            ["HOME"] = _temp.Path,
            ["USERPROFILE"] = _temp.Path,
            ["XDG_CONFIG_HOME"] = _temp.Combine(".config"),
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = null,
        };
        _git = new GitService(new ProcessRunner(NullLogger<ProcessRunner>.Instance), new ToolLocator(), isolated);
        _clone = new CloneService(_git, NullLogger<CloneService>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Builds_the_alias_url_and_default_target()
    {
        var work = TestProfiles.Create("work", @"C:\h", @"D:\work", @"D:\other");
        var repo = new RepoRef("github.com", "acme", "api");

        Assert.Equal("git@github-work:acme/api.git", CloneService.RemoteUrlFor(work, repo));
        Assert.Equal(@"D:\work\api", CloneService.DefaultTargetFor(work, repo));
        Assert.Equal(@"E:\clones\api", CloneService.DefaultTargetFor(work with { DefaultCloneFolder = @"E:\clones" }, repo));
        Assert.Null(CloneService.DefaultTargetFor(work with { Folders = [] }, repo));
    }

    [Theory]
    [InlineData("remote: Counting objects:  50% (5/10)", ClonePhase.Counting, 50, 2.5)]
    [InlineData("Receiving objects:  45% (450/1000), 1.20 MiB | 2.30 MiB/s", ClonePhase.Receiving, 45, 41.5)]
    [InlineData("Receiving objects: 100% (1000/1000), 3.10 MiB | 2.30 MiB/s, done.", ClonePhase.Receiving, 100, 80)]
    [InlineData("Resolving deltas: 100% (5/5), done.", ClonePhase.Resolving, 100, 95)]
    [InlineData("Updating files:  10% (1/10)", ClonePhase.Updating, 10, 95.5)]
    public void Parses_git_progress_lines(string line, ClonePhase phase, int percent, double overall)
    {
        Assert.Equal(new CloneProgress(phase, percent, overall), CloneService.ParseProgress(line));
    }

    [Theory]
    [InlineData("Cloning into 'api'...")]
    [InlineData("remote: Enumerating objects: 12, done.")]
    public void Other_lines_are_not_progress(string line)
    {
        Assert.Null(CloneService.ParseProgress(line));
    }

    [Theory]
    [InlineData("Host key verification failed.\nfatal: Could not read from remote repository.", CloneFailure.HostKeyVerification)]
    [InlineData("git@github.com: Permission denied (publickey).\nfatal: Could not read from remote repository.", CloneFailure.PermissionDenied)]
    [InlineData("ERROR: Repository not found.\nfatal: Could not read from remote repository.", CloneFailure.RepositoryNotFound)]
    [InlineData("ssh: Could not resolve hostname github-work: No such host is known.", CloneFailure.HostNotResolved)]
    [InlineData("fatal: something else", CloneFailure.Other)]
    public void Classifies_ssh_and_github_failures(string stderr, CloneFailure expected)
    {
        Assert.Equal(expected, CloneService.Classify(stderr));
    }

    [Fact]
    public async Task Clones_a_real_repository_and_reports_progress()
    {
        var source = await CreateSourceRepositoryAsync();
        var target = _temp.Combine("clones", "copy");
        var updates = new List<CloneProgress>();
        var output = new List<string>();

        await _clone.CloneAsync(new Uri(source).AbsoluteUri, target, new Synchronous<CloneProgress>(updates.Add), new Synchronous<string>(output.Add));

        Assert.True(File.Exists(Path.Combine(target, "file-2.txt")));
        Assert.Equal(ClonePhase.Starting, updates[0].Phase);
        Assert.Contains(updates, u => u.Phase == ClonePhase.Receiving && u.Percent == 100);
        Assert.True(updates.Select(u => u.Overall).SequenceEqual(updates.Select(u => u.Overall).Order()), "overall progress never goes back");
        Assert.Contains(output, line => line.StartsWith("Cloning into", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_clone_leaves_no_folder_behind()
    {
        var target = _temp.Combine("clones", "missing");

        var ex = await Assert.ThrowsAsync<CloneFailedException>(
            () => _clone.CloneAsync(new Uri(_temp.Combine("does-not-exist")).AbsoluteUri, target));

        Assert.Equal(CloneFailure.Other, ex.Failure);
        Assert.Contains("does not appear to be a git repository", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task Refuses_a_target_folder_that_is_not_empty()
    {
        var target = _temp.Combine("busy");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "mine");

        var ex = await Assert.ThrowsAsync<CloneFailedException>(() => _clone.CloneAsync("git@github-x:a/b.git", target));

        Assert.Equal(CloneFailure.TargetNotEmpty, ex.Failure);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    private async Task<string> CreateSourceRepositoryAsync()
    {
        var source = _temp.Combine("source");
        Directory.CreateDirectory(source);
        Assert.True((await _git.RunAsync(["init", "-q"], source)).Succeeded);
        for (var i = 1; i <= 2; i++)
        {
            File.WriteAllText(Path.Combine(source, $"file-{i}.txt"), new string('x', 5000 * i));
            Assert.True((await _git.RunAsync(["add", "."], source)).Succeeded);
            Assert.True((await _git.RunAsync(["-c", "user.name=T", "-c", "user.email=t@example.com", "commit", "-q", "-m", $"c{i}"], source)).Succeeded);
        }

        return source;
    }

    private sealed class Synchronous<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
        {
            lock (this)
            {
                report(value);
            }
        }
    }
}
