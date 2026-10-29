using GitPersona.Core.ConfigFiles;
using GitPersona.Core.Git;
using GitPersona.Core.Hooks;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Models;
using GitPersona.Core.Tests.Hooks;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.Integration;

/// <summary>
/// Real commits and pushes through Git for Windows with the generated hooks, against a throwaway HOME.
/// The hooks folder path contains a space on purpose.
/// </summary>
public sealed class CommitGuardTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly UserPaths _home;
    private readonly HookService _hooks;
    private readonly GitService _git;
    private readonly Profile _work;
    private readonly Profile _personal;

    public CommitGuardTests()
    {
        _home = new UserPaths(_temp.Combine("home"));
        _hooks = new HookService(new AppPaths(_temp.Combine("app data")), NullLogger<HookService>.Instance);
        _git = new GitService(new ProcessRunner(NullLogger<ProcessRunner>.Instance), new ToolLocator(), new Dictionary<string, string?>
        {
            ["HOME"] = _home.HomeDirectory,
            ["USERPROFILE"] = _home.HomeDirectory,
            ["XDG_CONFIG_HOME"] = _temp.Combine("home", ".config"),
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = null,
            ["GIT_AUTHOR_EMAIL"] = null,
            ["GIT_COMMITTER_EMAIL"] = null,
        });
        _work = TestProfiles.Create("work", _home.HomeDirectory, _temp.Combine("Work Repos"));
        _personal = TestProfiles.Create("personal", _home.HomeDirectory);
    }

    public void Dispose() => _temp.Dispose();

    private void Install(bool guardEnabled = true, string? previousHooksPath = null)
    {
        Directory.CreateDirectory(_home.SshDirectory);
        foreach (var profile in new[] { _work, _personal })
        {
            File.WriteAllText(profile.SshKeyPath, "dummy");
        }

        var plan = new ConfigPlanner(_home).Plan([_work, _personal], _hooks.HooksDirectory);
        new ConfigWriter(new BackupManager(), NullLogger<ConfigWriter>.Instance).Apply(plan.Changes);
        _hooks.Install([_work, _personal], HookServiceTests.Messages, previousHooksPath, guardEnabled);
    }

    private async Task<string> RepoAsync(string path, string? remote = null)
    {
        Directory.CreateDirectory(path);
        await Git(path, "init", "-q");
        if (remote is not null)
        {
            await Git(path, "remote", "add", "origin", remote);
        }

        return path;
    }

    private async Task Git(string repo, params string[] args)
    {
        var result = await _git.RunAsync(args, repo);
        Assert.True(result.Succeeded, $"git {string.Join(' ', args)} failed: {result.StandardError}");
    }

    private Task<ProcessResult> CommitAsync(string repo, params string[] extra) =>
        _git.RunAsync(["-c", "user.name=Tester", "commit", "--allow-empty", "-m", "test commit", .. extra], repo);

    private static void WriteHook(string repo, string name, string body) =>
        File.WriteAllText(Path.Combine(repo, ".git", "hooks", name), "#!/bin/sh\n" + body.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");

    [Fact]
    public async Task The_profiles_own_identity_commits()
    {
        Install();
        var repo = await RepoAsync(_temp.Combine("Work Repos", "api"));

        var result = await CommitAsync(repo);

        Assert.True(result.Succeeded, result.StandardError);
    }

    [Fact]
    public async Task A_wrong_local_email_is_blocked_with_an_explanation()
    {
        Install();
        var repo = await RepoAsync(_temp.Combine("Work Repos", "api"));
        await Git(repo, "config", "user.email", "wrong@example.com");

        var result = await CommitAsync(repo);

        Assert.False(result.Succeeded);
        Assert.Contains(HookServiceTests.Messages.Blocked, result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Profile work (github-work)", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Expected: work@example.com", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("Current: wrong@example.com", result.StandardError, StringComparison.Ordinal);
        Assert.Contains("git config user.email \"work@example.com\"", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_author_email_git_would_use_is_checked_including_the_environment()
    {
        Install();
        var repo = await RepoAsync(_temp.Combine("Work Repos", "api"));

        var result = await _git.RunAsync(
            ["-c", "user.name=Tester", "commit", "--allow-empty", "-m", "env"],
            repo,
            new Dictionary<string, string?> { ["GIT_AUTHOR_EMAIL"] = "from-env@example.com" });

        Assert.False(result.Succeeded);
        Assert.Contains("Current: from-env@example.com", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_verify_still_lets_the_user_commit()
    {
        Install();
        var repo = await RepoAsync(_temp.Combine("Work Repos", "api"));
        await Git(repo, "config", "user.email", "wrong@example.com");

        Assert.True((await CommitAsync(repo, "--no-verify")).Succeeded);
    }

    [Fact]
    public async Task Repositories_outside_every_profile_are_not_checked()
    {
        Install();
        var repo = await RepoAsync(_temp.Combine("elsewhere", "tool"), "https://github.com/someone/tool.git");
        await Git(repo, "config", "user.email", "anyone@example.com");

        Assert.True((await CommitAsync(repo)).Succeeded);
    }

    [Fact]
    public async Task The_remote_alias_decides_outside_profile_folders()
    {
        Install();
        var repo = await RepoAsync(_temp.Combine("elsewhere", "dotfiles"), "git@github-personal:me/dotfiles.git");
        await Git(repo, "config", "user.email", "work@example.com");

        var result = await CommitAsync(repo);

        Assert.False(result.Succeeded);
        Assert.Contains("Expected: personal@example.com", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_repositorys_own_pre_commit_hook_runs_after_the_check_and_can_still_block()
    {
        Install();
        var repo = await RepoAsync(_temp.Combine("Work Repos", "api"));
        WriteHook(repo, "pre-commit", "echo ran > \"$(git rev-parse --git-dir)/marker\"");

        Assert.True((await CommitAsync(repo)).Succeeded);
        Assert.True(File.Exists(Path.Combine(repo, ".git", "marker")));

        WriteHook(repo, "pre-commit", "echo lint failed >&2\nexit 1");
        var blocked = await CommitAsync(repo);
        Assert.False(blocked.Succeeded);
        Assert.Contains("lint failed", blocked.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_hooks_receive_their_arguments()
    {
        Install();
        var repo = await RepoAsync(_temp.Combine("Work Repos", "api"));
        WriteHook(repo, "commit-msg", "cp \"$1\" \"$(git rev-parse --git-dir)/seen-message\"");

        Assert.True((await CommitAsync(repo)).Succeeded);
        Assert.Contains("test commit", File.ReadAllText(Path.Combine(repo, ".git", "seen-message")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pre_push_input_reaches_the_repository_hook_and_the_previous_global_hooks()
    {
        var previous = _temp.Combine("old hooks");
        Directory.CreateDirectory(previous);
        File.WriteAllText(Path.Combine(previous, "pre-push"), "#!/bin/sh\ncat > \"" + _temp.Combine("from-previous").Replace('\\', '/') + "\"\n");
        Install(previousHooksPath: previous);
        var remote = _temp.Combine("remote.git");
        Directory.CreateDirectory(remote);
        await Git(remote, "init", "-q", "--bare");
        var repo = await RepoAsync(_temp.Combine("Work Repos", "api"), remote);
        WriteHook(repo, "pre-push", "cat > \"$(git rev-parse --git-dir)/from-repo\"");
        Assert.True((await CommitAsync(repo)).Succeeded);

        await Git(repo, "push", "-q", "origin", "HEAD:refs/heads/main");

        var fromRepo = File.ReadAllText(Path.Combine(repo, ".git", "from-repo"));
        Assert.Contains("refs/heads/main", fromRepo, StringComparison.Ordinal);
        Assert.Equal(fromRepo, File.ReadAllText(_temp.Combine("from-previous")));
    }

    [Fact]
    public async Task Worktrees_use_the_main_repositorys_hooks()
    {
        Install();
        var repo = await RepoAsync(_temp.Combine("Work Repos", "api"));
        Assert.True((await CommitAsync(repo)).Succeeded);
        WriteHook(repo, "pre-commit", "echo ran >> \"$(git rev-parse --git-common-dir)/worktree-marker\"");
        var worktree = _temp.Combine("Work Repos", "api-feature");
        await Git(repo, "worktree", "add", "-q", "-b", "feature", worktree);

        Assert.True((await CommitAsync(worktree)).Succeeded);
        Assert.True(File.Exists(Path.Combine(repo, ".git", "worktree-marker")));
    }

    [Fact]
    public async Task With_the_guard_off_only_the_repositorys_hooks_run()
    {
        Install(guardEnabled: false);
        var repo = await RepoAsync(_temp.Combine("Work Repos", "api"));
        await Git(repo, "config", "user.email", "wrong@example.com");
        WriteHook(repo, "pre-commit", "echo ran > \"$(git rev-parse --git-dir)/marker\"");

        Assert.True((await CommitAsync(repo)).Succeeded);
        Assert.True(File.Exists(Path.Combine(repo, ".git", "marker")));
    }

    [Fact]
    public async Task Hook_files_that_git_would_not_run_are_not_run()
    {
        Install();
        var repo = await RepoAsync(_temp.Combine("Work Repos", "api"));
        File.WriteAllText(Path.Combine(repo, ".git", "hooks", "pre-commit"), "exit 1\n");

        Assert.True((await CommitAsync(repo)).Succeeded);
    }
}
