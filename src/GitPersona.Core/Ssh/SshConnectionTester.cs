using System.Text.RegularExpressions;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Models;

namespace GitPersona.Core.Ssh;

public enum SshTestOutcome
{
    /// <summary>GitHub greeted the account the profile expects.</summary>
    Authenticated,

    /// <summary>GitHub greeted a different account: the key belongs to someone else.</summary>
    WrongAccount,

    /// <summary>The key is not on any account.</summary>
    PermissionDenied,

    /// <summary>The profile's IdentityFile does not exist.</summary>
    KeyFileMissing,

    /// <summary>The server's host key is not in known_hosts.</summary>
    HostKeyUnknown,

    /// <summary>known_hosts has a different key for the server.</summary>
    HostKeyChanged,

    /// <summary>The alias is not defined (configuration not applied) or DNS failed.</summary>
    HostNotResolved,

    ConnectionFailed,
    Other,
}

/// <param name="Login">The account GitHub greeted, when it did.</param>
public sealed record SshTestResult(SshTestOutcome Outcome, string? Login, string Output, SshCommand Ssh);

/// <summary><c>ssh -T git@&lt;alias&gt;</c> with the ssh git uses and the configuration GitPersona manages.</summary>
public sealed partial class SshConnectionTester(IProcessRunner runner, GitSshResolver resolver, UserPaths paths)
{
    [GeneratedRegex(@"Hi (?<login>[^!\s]+)! You've successfully authenticated", RegexOptions.CultureInvariant)]
    private static partial Regex Greeting();

    public async Task<SshTestResult> TestAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var ssh = await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false) ?? throw new ToolNotFoundException("ssh");
        string[] arguments =
        [
            .. ssh.Arguments,
            "-T",
            // Never prompt (passphrase, unknown host key); fail fast instead.
            "-o", "BatchMode=yes",
            "-o", "ConnectTimeout=10",
            // Exactly the files GitPersona manages, also when GITPERSONA_HOME_DIR points elsewhere.
            "-F", SshConfigBuilder.ToSshPath(paths.SshConfigFile),
            "-o", "UserKnownHostsFile=" + SshConfigBuilder.ToSshPath(paths.KnownHostsFile),
            $"git@{profile.HostAlias}",
        ];
        var result = await runner.RunAsync(new ProcessRequest(ssh.FileName, arguments) { Timeout = TimeSpan.FromSeconds(25) }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Interpret(result.StandardError + result.StandardOutput, profile.GitHubUsername, ssh);
    }

    /// <summary>GitHub answers a successful <c>ssh -T</c> with exit code 1 and a greeting, so the text decides.</summary>
    public static SshTestResult Interpret(string output, string expectedLogin, SshCommand ssh)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (Greeting().Match(output) is { Success: true } greeting)
        {
            var login = greeting.Groups["login"].Value;
            var matches = string.IsNullOrWhiteSpace(expectedLogin) || login.Equals(expectedLogin.Trim(), StringComparison.OrdinalIgnoreCase);
            return new SshTestResult(matches ? SshTestOutcome.Authenticated : SshTestOutcome.WrongAccount, login, output.Trim(), ssh);
        }

        bool Has(string text) => output.Contains(text, StringComparison.OrdinalIgnoreCase);
        var outcome =
            Has("REMOTE HOST IDENTIFICATION HAS CHANGED") ? SshTestOutcome.HostKeyChanged
            : Has("Host key verification failed") || Has("No ED25519 host key is known") || Has("host key is known") ? SshTestOutcome.HostKeyUnknown
            : Has("Could not resolve hostname") ? SshTestOutcome.HostNotResolved
            : Has("no such identity") ? SshTestOutcome.KeyFileMissing
            : Has("Permission denied (publickey") ? SshTestOutcome.PermissionDenied
            : Has("Connection timed out") || Has("Connection refused") || Has("Network is unreachable") ? SshTestOutcome.ConnectionFailed
            : SshTestOutcome.Other;
        return new SshTestResult(outcome, null, output.Trim(), ssh);
    }
}
