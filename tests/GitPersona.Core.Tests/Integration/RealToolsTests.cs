using GitPersona.Core.ConfigFiles;
using GitPersona.Core.Git;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Models;
using GitPersona.Core.Ssh;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.Integration;

/// <summary>
/// Runs the generated configuration through the real Git for Windows and OpenSSH binaries,
/// against a throwaway HOME so the developer's own configuration is never read or touched.
/// </summary>
public sealed class RealToolsTests : IDisposable
{
    private readonly TempDirectory _home = new();
    private readonly UserPaths _paths;
    private readonly ToolLocator _tools = new();
    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);
    private readonly GitService _git;
    private readonly Dictionary<string, string?> _isolated;

    public RealToolsTests()
    {
        _paths = new UserPaths(_home.Path);
        _git = new GitService(_runner, _tools);
        _isolated = new Dictionary<string, string?>
        {
            ["HOME"] = _home.Path,
            ["USERPROFILE"] = _home.Path,
            ["XDG_CONFIG_HOME"] = _home.Combine(".config"),
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = null,
        };
    }

    public void Dispose() => _home.Dispose();

    private void Apply(params Profile[] profiles)
    {
        Directory.CreateDirectory(_paths.SshDirectory);
        foreach (var profile in profiles)
        {
            File.WriteAllText(profile.SshKeyPath, "dummy");
        }

        var plan = new ConfigPlanner(_paths).Plan(profiles);
        Assert.False(plan.IsBlocked, string.Join(", ", plan.Issues));
        new ConfigWriter(new BackupManager(), NullLogger<ConfigWriter>.Instance).Apply(plan.Changes);
    }

    private async Task<string> InitRepoAsync(string directory, string? remote = null)
    {
        Directory.CreateDirectory(directory);
        Assert.True((await _git.RunAsync(["init", "-q"], directory, _isolated)).Succeeded);
        if (remote is not null)
        {
            Assert.True((await _git.RunAsync(["remote", "add", "origin", remote], directory, _isolated)).Succeeded);
        }

        return directory;
    }

    private Task<string?> EmailAsync(string repository) => _git.GetConfigAsync(repository, "user.email", _isolated);

    [Fact]
    public async Task Repositories_get_the_identity_of_their_folder_and_nested_folders_win()
    {
        Apply(
            TestProfiles.Create("personal", _home.Path, _home.Combine("repos")),
            TestProfiles.Create("work", _home.Path, _home.Combine("repos", "work")));

        Assert.Equal("personal@example.com", await EmailAsync(await InitRepoAsync(_home.Combine("repos", "hobby"))));
        Assert.Equal("work@example.com", await EmailAsync(await InitRepoAsync(_home.Combine("repos", "work", "api"))));
    }

    [Fact]
    public async Task Folder_rules_match_case_insensitively_and_literally()
    {
        Apply(TestProfiles.Create("work", _home.Path, _home.Combine("My Repos", "old[2024]")));

        var repo = await InitRepoAsync(_home.Combine("My Repos", "old[2024]", "app"));
        Assert.Equal("work@example.com", await EmailAsync(repo.ToUpperInvariant()));
        Assert.Null(await EmailAsync(await InitRepoAsync(_home.Combine("My Repos", "old2", "app"))));
    }

    [Fact]
    public async Task The_remote_alias_decides_the_identity_anywhere_and_beats_the_folder()
    {
        Apply(
            TestProfiles.Create("personal", _home.Path),
            TestProfiles.Create("work", _home.Path, _home.Combine("work")));

        Assert.Equal("personal@example.com", await EmailAsync(await InitRepoAsync(_home.Combine("elsewhere"), "git@github-personal:me/dotfiles.git")));
        Assert.Equal("personal@example.com", await EmailAsync(await InitRepoAsync(_home.Combine("work", "side"), "ssh://git@github-personal/me/side.git")));
        Assert.Equal("work@example.com", await EmailAsync(await InitRepoAsync(_home.Combine("work", "main"), "git@github.com:acme/main.git")));
    }

    [Fact]
    public async Task Repositories_outside_every_rule_are_left_alone()
    {
        Apply(TestProfiles.Create("work", _home.Path, _home.Combine("work")));

        Assert.Null(await EmailAsync(await InitRepoAsync(_home.Combine("other"), "https://github.com/acme/x.git")));
    }

    public static TheoryData<string> SshBuilds => ["windows", "git-bundled"];

    [Theory]
    [MemberData(nameof(SshBuilds))]
    public async Task Both_ssh_builds_resolve_the_alias_to_the_profile_key(string build)
    {
        var ssh = build == "windows" ? _tools.WindowsOpenSsh("ssh") : _tools.GitBundledOpenSsh("ssh");
        Assert.NotNull(ssh);
        var profile = TestProfiles.Create("work", _home.Path);
        Apply(profile);

        var result = await _runner.RunAsync(new ProcessRequest(ssh, ["-F", _paths.SshConfigFile, "-G", "github-work"]));

        Assert.True(result.Succeeded, result.StandardError);
        var settings = result.StandardOutput.Split('\n', StringSplitOptions.TrimEntries);
        Assert.Contains("hostname github.com", settings);
        Assert.Contains("user git", settings);
        Assert.Contains("identitiesonly yes", settings);
        Assert.Contains(settings, line => line.Equals("identityfile " + SshConfigBuilder.ToSshPath(profile.SshKeyPath), StringComparison.OrdinalIgnoreCase));
    }
}
