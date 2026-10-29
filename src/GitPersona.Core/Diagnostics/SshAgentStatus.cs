using System.Globalization;
using System.Text.RegularExpressions;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;

namespace GitPersona.Core.Diagnostics;

public enum ServiceStartMode
{
    Unknown,
    Automatic,
    Manual,
    Disabled,
}

/// <param name="Installed">Whether the Windows "OpenSSH Authentication Agent" service exists.</param>
/// <param name="LoadedFingerprints">SHA256 fingerprints of keys loaded into the agent (when it runs).</param>
public sealed record SshAgentStatus(bool Installed, bool Running, ServiceStartMode StartMode, IReadOnlyList<string> LoadedFingerprints);

/// <summary>The Windows ssh-agent service, read without admin rights (sc.exe and ssh-add).</summary>
public sealed partial class SshAgentProbe(IProcessRunner runner, ToolLocator tools)
{
    private const string ServiceName = "ssh-agent";

    [GeneratedRegex(@"^\s*(?<key>STATE|START_TYPE)\s*:\s*(?<code>\d+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ScField();

    [GeneratedRegex(@"SHA256:[A-Za-z0-9+/]+", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintPattern();

    public async Task<SshAgentStatus> ReadAsync(CancellationToken cancellationToken = default)
    {
        var sc = Path.Combine(Environment.SystemDirectory, "sc.exe");
        var query = await runner.RunAsync(new ProcessRequest(sc, ["query", ServiceName]), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!query.Succeeded)
        {
            return new SshAgentStatus(false, false, ServiceStartMode.Unknown, []);
        }

        var config = await runner.RunAsync(new ProcessRequest(sc, ["qc", ServiceName]), cancellationToken: cancellationToken).ConfigureAwait(false);
        var running = Field(query.StandardOutput, "STATE") == 4;
        var mode = Field(config.StandardOutput, "START_TYPE") switch
        {
            2 => ServiceStartMode.Automatic,
            3 => ServiceStartMode.Manual,
            4 => ServiceStartMode.Disabled,
            _ => ServiceStartMode.Unknown,
        };

        IReadOnlyList<string> loaded = [];
        if (running && tools.WindowsOpenSsh("ssh-add") is { } sshAdd)
        {
            // Exit code 1: the agent has no identities.
            var list = await runner.RunAsync(new ProcessRequest(sshAdd, ["-l", "-E", "sha256"]), cancellationToken: cancellationToken).ConfigureAwait(false);
            loaded = FingerprintPattern().Matches(list.StandardOutput).Select(m => m.Value).ToList();
        }

        return new SshAgentStatus(true, running, mode, loaded);
    }

    /// <summary>The numeric code of a field in sc.exe output; the numbers do not depend on the Windows language.</summary>
    internal static int? Field(string scOutput, string key) =>
        ScField().Matches(scOutput).FirstOrDefault(m => m.Groups["key"].Value == key) is { } match
            ? int.Parse(match.Groups["code"].Value, CultureInfo.InvariantCulture)
            : null;
}
