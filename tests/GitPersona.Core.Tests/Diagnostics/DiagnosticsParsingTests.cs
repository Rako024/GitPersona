using GitPersona.Core.Diagnostics;
using GitPersona.Core.GitConfig;
using GitPersona.Core.GitHub;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Ssh;

namespace GitPersona.Core.Tests.Diagnostics;

public class DiagnosticsParsingTests
{
    private static readonly SshCommand AnySsh = new(@"C:\Program Files\Git\usr\bin\ssh.exe", [], SshCommandSource.GitBundled);

    [Fact]
    public void Reads_gh_accounts()
    {
        const string json = """{"hosts":{"github.com":[{"state":"success","active":true,"host":"github.com","login":"octocat"},{"state":"success","active":false,"host":"github.com","login":"work-me"}],"git.corp.example":[{"login":"corp","active":true}]}}""";

        Assert.Equal(
            [new GhAccount("github.com", "octocat", true), new GhAccount("github.com", "work-me", false), new GhAccount("git.corp.example", "corp", true)],
            GhCliService.ParseAccounts(json));
        Assert.Empty(GhCliService.ParseAccounts(""));
        Assert.Empty(GhCliService.ParseAccounts("""{"hosts":{}}"""));
    }

    [Theory]
    [InlineData("\"C:/Program Files/OpenSSH/ssh.exe\" -i key", new[] { "C:/Program Files/OpenSSH/ssh.exe", "-i", "key" })]
    [InlineData("C:/Windows/System32/OpenSSH/ssh.exe", new[] { "C:/Windows/System32/OpenSSH/ssh.exe" })]
    [InlineData("ssh -o 'ProxyCommand=a b'", new[] { "ssh", "-o", "ProxyCommand=a b" })]
    public void Splits_ssh_command_lines(string commandLine, string[] expected)
    {
        Assert.Equal(expected, GitSshResolver.Tokenize(commandLine));
    }

    [Fact]
    public void Windows_openssh_is_recognised_in_either_slash_style()
    {
        Assert.True(new SshCommand("C:/Windows/System32/OpenSSH/ssh.exe", [], SshCommandSource.CoreSshCommand).IsWindowsOpenSsh);
        Assert.True(new SshCommand(@"C:\WINDOWS\System32\OpenSSH\ssh.exe", [], SshCommandSource.WindowsOpenSsh).IsWindowsOpenSsh);
        Assert.False(AnySsh.IsWindowsOpenSsh);
    }

    [Theory]
    [InlineData("Hi work-me! You've successfully authenticated, but GitHub does not provide shell access.", "work-me", SshTestOutcome.Authenticated, "work-me")]
    [InlineData("Hi Work-Me! You've successfully authenticated, but GitHub does not provide shell access.", "work-me", SshTestOutcome.Authenticated, "Work-Me")]
    [InlineData("Hi personal! You've successfully authenticated, but GitHub does not provide shell access.", "work-me", SshTestOutcome.WrongAccount, "personal")]
    [InlineData("git@github.com: Permission denied (publickey).", "work-me", SshTestOutcome.PermissionDenied, null)]
    [InlineData("no such identity: C:/Users/me/.ssh/id_gone: No such file or directory\r\ngit@github.com: Permission denied (publickey).", "x", SshTestOutcome.KeyFileMissing, null)]
    [InlineData("No ED25519 host key is known for github.com and you have requested strict checking.\r\nHost key verification failed.", "x", SshTestOutcome.HostKeyUnknown, null)]
    [InlineData("@@@@\r\n@    WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!     @\r\nHost key verification failed.", "x", SshTestOutcome.HostKeyChanged, null)]
    [InlineData("ssh: Could not resolve hostname github-work: No such host is known.", "x", SshTestOutcome.HostNotResolved, null)]
    [InlineData("ssh: connect to host github.com port 22: Connection timed out", "x", SshTestOutcome.ConnectionFailed, null)]
    public void Interprets_ssh_T(string output, string expectedLogin, SshTestOutcome outcome, string? login)
    {
        var result = SshConnectionTester.Interpret(output, expectedLogin, AnySsh);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(login, result.Login);
    }

    [Fact]
    public void Reads_numeric_codes_from_sc_exe()
    {
        const string query = "SERVICE_NAME: ssh-agent\r\n        TYPE               : 10  WIN32_OWN_PROCESS\r\n        STATE              : 4  RUNNING\r\n";
        const string config = "        START_TYPE         : 4   DISABLED\r\n";

        Assert.Equal(4, SshAgentProbe.Field(query, "STATE"));
        Assert.Equal(4, SshAgentProbe.Field(config, "START_TYPE"));
        Assert.Null(SshAgentProbe.Field(query, "START_TYPE"));
    }

    [Fact]
    public void Core_section_holds_the_hooks_path_and_the_ssh_command()
    {
        var lines = GitConfigBuilder.GlobalBlock([], new UserPaths(@"C:\Users\me"), @"C:\Users\me\AppData\Roaming\GitPersona\hooks", @"C:\Windows\System32\OpenSSH\ssh.exe");

        Assert.Equal("[core]", lines[0]);
        Assert.Contains("\thooksPath = C:/Users/me/AppData/Roaming/GitPersona/hooks", lines);
        Assert.Contains("\tsshCommand = C:/Windows/System32/OpenSSH/ssh.exe", lines);
    }
}
