using GitPersona.Core.ConfigFiles;
using GitPersona.Core.GitConfig;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.ConfigFiles;

public sealed class ConfigPlannerTests : IDisposable
{
    private readonly TempDirectory _home = new();
    private readonly UserPaths _paths;
    private readonly ConfigPlanner _planner;
    private readonly ConfigWriter _writer = new(new BackupManager(), NullLogger<ConfigWriter>.Instance);

    public ConfigPlannerTests()
    {
        _paths = new UserPaths(_home.Path);
        _planner = new ConfigPlanner(_paths);
    }

    public void Dispose() => _home.Dispose();

    private Profile Work() => WithKey(TestProfiles.Create("work", _home.Path, _home.Combine("work")));

    private Profile WithKey(Profile profile)
    {
        Directory.CreateDirectory(_paths.SshDirectory);
        File.WriteAllText(profile.SshKeyPath, "dummy private key");
        return profile;
    }

    [Fact]
    public void Creates_all_files_in_an_empty_home()
    {
        var plan = _planner.Plan([Work()]);

        Assert.False(plan.IsBlocked);
        Assert.Equal(
            [ConfigFileRole.SshConfig, ConfigFileRole.GlobalGitConfig, ConfigFileRole.ProfileGitConfig],
            plan.Changes.Pending.Select(c => c.Role));
        Assert.All(plan.Changes.Pending, c => Assert.Equal(ConfigChangeKind.Create, c.Kind));
    }

    [Fact]
    public void Keeps_the_users_content_with_ssh_block_first_and_git_block_last()
    {
        Directory.CreateDirectory(_paths.SshDirectory);
        File.WriteAllText(_paths.SshConfigFile, "Host *\n    ServerAliveInterval 30\n");
        File.WriteAllText(_paths.GlobalGitConfigFile, "[user]\n\temail = me@corp.example\n");

        _writer.Apply(_planner.Plan([Work()]).Changes);

        var ssh = File.ReadAllText(_paths.SshConfigFile);
        var git = File.ReadAllText(_paths.GlobalGitConfigFile);
        Assert.StartsWith(ManagedBlock.StartMarker, ssh, StringComparison.Ordinal);
        Assert.EndsWith("Host *\n    ServerAliveInterval 30\n", ssh, StringComparison.Ordinal);
        Assert.StartsWith("[user]\n\temail = me@corp.example\n" + ManagedBlock.StartMarker, git, StringComparison.Ordinal);
    }

    [Fact]
    public void Planning_again_after_applying_changes_nothing()
    {
        var profiles = new[] { Work() };
        _writer.Apply(_planner.Plan(profiles).Changes);

        var plan = _planner.Plan(profiles);

        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void Refuses_to_overwrite_a_profile_file_it_did_not_write()
    {
        File.WriteAllText(_paths.ProfileGitConfigFile("work"), "[user]\n\tname = hand written\n");

        var plan = _planner.Plan([Work()]);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Issues, i => i.Code == PlanIssueCode.ForeignProfileConfigFile && i.ProfileId == "work");
        Assert.DoesNotContain(plan.Changes.Changes, c => c.Role == ConfigFileRole.ProfileGitConfig);
    }

    [Fact]
    public void Deletes_its_own_files_of_removed_profiles_but_nothing_else()
    {
        var old = _paths.ProfileGitConfigFile("old");
        var foreign = _paths.ProfileGitConfigFile("foreign");
        var backup = old + ".20261029-153012.bak";
        File.WriteAllText(old, GitConfigBuilder.ProfileFile(TestProfiles.Create("old", _home.Path)));
        File.WriteAllText(foreign, "[user]\n");
        File.WriteAllText(backup, GitConfigBuilder.ProfileFile(TestProfiles.Create("old", _home.Path)));

        var deletions = _planner.Plan([Work()]).Changes.Pending.Where(c => c.Kind == ConfigChangeKind.Delete).ToList();

        Assert.Equal(old, Assert.Single(deletions).FilePath);
    }

    [Fact]
    public void Removing_every_profile_removes_the_blocks()
    {
        File.WriteAllText(_paths.GlobalGitConfigFile, "[core]\n\tautocrlf = true\n");
        var profiles = new[] { Work() };
        _writer.Apply(_planner.Plan(profiles).Changes);

        _writer.Apply(_planner.Plan([]).Changes);

        Assert.Equal("[core]\n\tautocrlf = true\n", File.ReadAllText(_paths.GlobalGitConfigFile));
        Assert.Equal(string.Empty, File.ReadAllText(_paths.SshConfigFile));
        Assert.False(File.Exists(_paths.ProfileGitConfigFile("work")));
    }

    [Fact]
    public void No_profiles_and_no_files_means_no_changes()
    {
        Assert.Empty(_planner.Plan([]).Changes.Changes);
    }

    [Fact]
    public void A_profile_without_a_key_is_reported_and_gets_no_host_block()
    {
        var plan = _planner.Plan([TestProfiles.Create("work", _home.Path) with { SshKeyPath = "" }]);

        Assert.Contains(plan.Issues, i => i.Code == PlanIssueCode.MissingSshKey && !i.IsBlocking);
        Assert.DoesNotContain(plan.Changes.Changes, c => c.Role == ConfigFileRole.SshConfig);
    }

    [Fact]
    public void A_missing_key_file_is_a_warning()
    {
        var plan = _planner.Plan([TestProfiles.Create("work", _home.Path)]);

        Assert.Contains(plan.Issues, i => i.Code == PlanIssueCode.SshKeyNotFound && !i.IsBlocking);
        Assert.False(plan.IsBlocked);
    }

    [Fact]
    public void Malformed_markers_block_the_plan()
    {
        File.WriteAllText(_paths.GlobalGitConfigFile, ManagedBlock.StartMarker + "\n[user]\n");

        var plan = _planner.Plan([Work()]);

        Assert.True(plan.IsBlocked);
        Assert.Contains(plan.Issues, i => i.Code == PlanIssueCode.MalformedBlock && i.Value == _paths.GlobalGitConfigFile);
    }

    [Fact]
    public void Warns_when_the_user_already_defines_the_alias()
    {
        Directory.CreateDirectory(_paths.SshDirectory);
        File.WriteAllText(_paths.SshConfigFile, "Host github-work\n    IdentityFile ~/.ssh/old\n");

        var plan = _planner.Plan([Work()]);

        Assert.Contains(plan.Issues, i => i.Code == PlanIssueCode.AliasDefinedOutsideBlock && i.Value == "github-work");
    }
}
