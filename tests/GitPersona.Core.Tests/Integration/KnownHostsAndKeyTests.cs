using System.Security.AccessControl;
using System.Security.Principal;
using GitPersona.Core.Diagnostics;
using GitPersona.Core.Git;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Models;
using GitPersona.Core.Security;
using GitPersona.Core.Ssh;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.Integration;

/// <summary>known_hosts handling with the real ssh-keygen, key ACLs and a full diagnostics run, all in a temp HOME.</summary>
public sealed class KnownHostsAndKeyTests : IDisposable
{
    // GitHub's host keys as published at https://api.github.com/meta (fetched 2026-10-29).
    private static readonly string[] GitHubKeys =
    [
        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl",
        "ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTYAAABBBEmKSENjQEezOmxkZMy7opKgwFB9nkt5YRrYMjNuG5N87uRgg6CLrbo5wAdT/y6v0mKV0U2w0WZ2YB/++Tpockg=",
        "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABgQCj7ndNxQowgcQnjshcLrqPEiiphnt+VTTvDP6mHBL9j1aNUkY4Ue1gvwnGLVlOhGeYrnZaMgRK6+PKCUXaDbC7qtbW8gIkhL7aGCsOr/C56SJMy/BCZfxd1nWzAOxSDPgVsmerOBYfNqltV9/hWCqBywINIR+5dIg6JTJ72pcEpEjcYgXkE2YEFXV1JHnsKgbLWNlhScqb2UmyRkQyytRLtL+38TGxkxCflmO+5Z8CSSNY7GidjMIZ7Q4zMjA2n1nGrlTDkzwDCsw+wqFPGQA179cnfGWOWRVruj16z6XyvxvjJwbz0wQZ75XK5tKSb7FNyeIEs4TT4jk+S4dhPeAUC5y+bDYirYgM4GC7uEnztnZyaVWQ7B381AK4Qdrwt51ZqExKbQpTUNn+EjqoTwvqNj4kqx5QUCI0ThS/YkOxJCXmPUWZbhjpCg56i+2aB6CmK2JGhn57K5mj0MNdBXA4/WnwH6XoPWJzK5Nyu2zB3nAZp+S5hpQs+p1vN1/wsjk=",
    ];

    // Some other ed25519 key, standing in for an outdated or intercepted github.com entry.
    private const string ForeignKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIJdD7y3aLq454yWBdwLWbieU1ebz9/cu7/QEXn9OIeZJ";

    private readonly TempDirectory _temp = new();
    private readonly UserPaths _paths;
    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);
    private readonly ToolLocator _tools = new();
    private readonly HttpClient _http = new();
    private readonly KnownHostsService _knownHosts;

    public KnownHostsAndKeyTests()
    {
        _paths = new UserPaths(_temp.Combine("home"));
        Directory.CreateDirectory(_paths.SshDirectory);
        _knownHosts = new KnownHostsService(_runner, _tools, _paths, _http, new BackupManager(), NullLogger<KnownHostsService>.Instance);
    }

    public void Dispose()
    {
        _http.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void Fingerprints_match_what_github_publishes()
    {
        Assert.True(GitHubKeys.Select(KnownHostsService.Fingerprint).ToHashSet().SetEquals(KnownHostsService.GitHubFingerprints));
    }

    [Fact]
    public async Task No_file_or_no_github_entry_means_missing()
    {
        Assert.Equal(KnownHostsState.Missing, (await _knownHosts.CheckGitHubAsync()).State);

        File.WriteAllText(_paths.KnownHostsFile, "tfs.corp.example ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIJdD7y3aLq454yWBdwLWbieU1ebz9/cu7/QEXn9OIeZJ\n");
        Assert.Equal(KnownHostsState.Missing, (await _knownHosts.CheckGitHubAsync()).State);
    }

    [Fact]
    public async Task Official_keys_are_current_even_when_hashed()
    {
        File.WriteAllText(_paths.KnownHostsFile, string.Join('\n', GitHubKeys.Select(k => "github.com " + k)) + "\n");
        await _runner.RunAsync(new ProcessRequest(_tools.RequireSshKeygen(), ["-H", "-f", _paths.KnownHostsFile]));
        Assert.DoesNotContain("github.com", File.ReadAllText(_paths.KnownHostsFile), StringComparison.Ordinal);

        Assert.Equal(KnownHostsState.Current, (await _knownHosts.CheckGitHubAsync()).State);
    }

    [Fact]
    public async Task A_foreign_github_key_is_a_mismatch_and_installing_replaces_only_github_entries()
    {
        var other = "tfs.corp.example " + GitHubKeys[1];
        File.WriteAllText(_paths.KnownHostsFile, $"{other}\ngithub.com {ForeignKey}");

        var before = await _knownHosts.CheckGitHubAsync();
        var backup = await _knownHosts.InstallGitHubKeysAsync(GitHubKeys);
        var after = await _knownHosts.CheckGitHubAsync();

        Assert.Equal(KnownHostsState.Mismatch, before.State);
        Assert.Equal([KnownHostsService.Fingerprint(ForeignKey)], before.UnknownFingerprints);
        Assert.Equal(KnownHostsState.Current, after.State);
        var text = File.ReadAllText(_paths.KnownHostsFile);
        Assert.Contains(other, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ForeignKey.Split(' ')[1], text, StringComparison.Ordinal);
        Assert.Contains(ForeignKey.Split(' ')[1], File.ReadAllText(backup!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keys_that_github_does_not_publish_are_never_installed()
    {
        await Assert.ThrowsAsync<HostKeyVerificationException>(() => _knownHosts.InstallGitHubKeysAsync([ForeignKey]));
        Assert.False(File.Exists(_paths.KnownHostsFile));
    }

    [Fact]
    public void A_key_readable_by_everyone_is_reported_and_can_be_restricted()
    {
        var key = _temp.Combine("home", ".ssh", "id_test");
        File.WriteAllText(key, "secret");
        Assert.Empty(KeyFilePermissions.LooseAccess(key));

        var info = new FileInfo(key);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
        info.SetAccessControl(security);
        Assert.Single(KeyFilePermissions.LooseAccess(key));

        KeyFilePermissions.Restrict(key);

        Assert.Empty(KeyFilePermissions.LooseAccess(key));
        Assert.Equal("secret", File.ReadAllText(key));
    }

    [Fact]
    public async Task A_full_diagnostics_run_reports_the_sandbox_problems()
    {
        var git = new GitService(_runner, _tools, new Dictionary<string, string?> { ["HOME"] = _paths.HomeDirectory, ["GIT_CONFIG_NOSYSTEM"] = "1" });
        var keys = new SshKeyService(_runner, _tools, _paths, NullLogger<SshKeyService>.Instance);
        var service = new DiagnosticsService(git, _tools, _runner, new GitSshResolver(git, _tools, _paths), _knownHosts,
            new SshAgentProbe(_runner, _tools), keys, new CredentialStore(), _paths, NullLogger<DiagnosticsService>.Instance);
        var key = await keys.GenerateAsync(_paths.DefaultSshKeyFile("work"), "w@example.com", "");
        Profile[] profiles =
        [
            TestProfiles.Create("work", _paths.HomeDirectory) with { SshKeyPath = key.PrivateKeyPath },
            TestProfiles.Create("gone", _paths.HomeDirectory) with { SshKeyPath = _temp.Combine("missing-key") },
        ];

        var codes = (await service.RunAsync(profiles)).Select(i => i.Code).ToList();

        Assert.Contains(DiagnosticCode.GitVersion, codes);
        Assert.Contains(DiagnosticCode.SshUsedByGit, codes);
        Assert.Contains(DiagnosticCode.KnownHostsMissing, codes);
        Assert.Contains(DiagnosticCode.KeyMissing, codes);
        Assert.Contains(DiagnosticCode.AgentNotNeeded, codes);
        Assert.DoesNotContain(DiagnosticCode.KeyPermissions, codes);
    }
}
