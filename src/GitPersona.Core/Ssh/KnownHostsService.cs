using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.Ssh;

public enum KnownHostsState
{
    /// <summary>No key for github.com: the first connection fails in non-interactive use ("Host key verification failed").</summary>
    Missing,

    /// <summary>Every github.com key in known_hosts is one of GitHub's published keys.</summary>
    Current,

    /// <summary>A github.com key that GitHub does not publish (e.g. the RSA key rotated in March 2023, or interception).</summary>
    Mismatch,
}

public sealed record KnownHostsReport(KnownHostsState State, IReadOnlyList<string> UnknownFingerprints);

public sealed class HostKeyVerificationException(string message) : Exception(message);

/// <summary>Keeps github.com's host keys in ~/.ssh/known_hosts current, verified against GitHub's published fingerprints.</summary>
public sealed class KnownHostsService(
    IProcessRunner runner,
    ToolLocator tools,
    UserPaths paths,
    HttpClient http,
    BackupManager backups,
    ILogger<KnownHostsService> logger)
{
    public const string GitHubHost = "github.com";

    /// <summary>
    /// From https://docs.github.com/authentication/keeping-your-account-and-data-secure/githubs-ssh-key-fingerprints
    /// (Ed25519, ECDSA, RSA since March 2023). Keys are fetched over HTTPS and accepted only when they match these.
    /// </summary>
    public static readonly IReadOnlySet<string> GitHubFingerprints = new HashSet<string>(StringComparer.Ordinal)
    {
        "SHA256:+DiY3wvvV6TuJJhbpZisF/zLDA0zPMSvHdkr4UvCOqU",
        "SHA256:p2QAMXNIC1TJYWeIOttrVc98/R1BUFWu3/LiyKgUfQM",
        "SHA256:uNiVztksCsDhcc0u9e8BujQXVUpKZIDTMczCvj3tD2s",
    };

    public async Task<KnownHostsReport> CheckGitHubAsync(CancellationToken cancellationToken = default)
    {
        var entries = await FindEntriesAsync(cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return new KnownHostsReport(KnownHostsState.Missing, []);
        }

        var unknown = entries.Select(e => e.Fingerprint).Where(f => !GitHubFingerprints.Contains(f)).Distinct().ToList();
        return new KnownHostsReport(unknown.Count == 0 ? KnownHostsState.Current : KnownHostsState.Mismatch, unknown);
    }

    /// <summary>GitHub's keys from api.github.com/meta, rejected unless every one matches a published fingerprint.</summary>
    /// <exception cref="HostKeyVerificationException">The keys did not match.</exception>
    public async Task<IReadOnlyList<string>> FetchGitHubKeysAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/meta");
        request.Headers.UserAgent.ParseAdd("GitPersona");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var meta = await response.Content.ReadFromJsonAsync<MetaResponse>(cancellationToken).ConfigureAwait(false);
        var keys = meta?.SshKeys ?? [];
        if (keys.Count == 0 || keys.Any(key => !GitHubFingerprints.Contains(Fingerprint(key))))
        {
            throw new HostKeyVerificationException("The keys from api.github.com do not match GitHub's published fingerprints.");
        }

        return keys;
    }

    /// <summary>Replaces every github.com entry with <paramref name="keys"/>; other hosts are untouched.</summary>
    /// <returns>The backup of the previous known_hosts, if there was one.</returns>
    public async Task<string?> InstallGitHubKeysAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Any(key => !GitHubFingerprints.Contains(Fingerprint(key))))
        {
            throw new HostKeyVerificationException("Refusing to install keys that GitHub does not publish.");
        }

        var file = paths.KnownHostsFile;
        Directory.CreateDirectory(paths.SshDirectory);
        var backup = backups.Backup(file);

        if ((await FindEntriesAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            // ssh-keygen -R also understands hashed host names; it leaves known_hosts.old next to the file.
            var removal = await runner.RunAsync(new ProcessRequest(tools.RequireSshKeygen(), ["-R", GitHubHost, "-f", file]), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!removal.Succeeded)
            {
                throw new IOException($"ssh-keygen -R failed: {removal.StandardError.Trim()}");
            }
        }

        var existing = File.Exists(file) ? await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false) : string.Empty;
        var builder = new StringBuilder(existing);
        if (existing.Length > 0 && !existing.EndsWith('\n'))
        {
            builder.Append('\n');
        }

        foreach (var key in keys)
        {
            builder.Append(GitHubHost).Append(' ').Append(key.Trim()).Append('\n');
        }

        AtomicFileWriter.Write(file, Encoding.ASCII.GetBytes(builder.ToString()));
        logger.LogInformation("Installed {Count} GitHub host keys in {File} (backup: {Backup})", keys.Count, file, backup ?? "none");
        return backup;
    }

    /// <summary>OpenSSH's SHA256 fingerprint of a public key line ("ssh-ed25519 AAAA… [comment]").</summary>
    public static string Fingerprint(string publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        var parts = publicKey.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var blob = parts.Length >= 2 ? parts[1] : parts[0];
        return "SHA256:" + Convert.ToBase64String(SHA256.HashData(Convert.FromBase64String(blob))).TrimEnd('=');
    }

    private async Task<IReadOnlyList<(string Line, string Fingerprint)>> FindEntriesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.KnownHostsFile))
        {
            return [];
        }

        // -F finds hashed entries too. Exit code 1 means "not found".
        var result = await runner.RunAsync(new ProcessRequest(tools.RequireSshKeygen(), ["-F", GitHubHost, "-f", paths.KnownHostsFile]), cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.StandardOutput.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 3)
            .Select(parts => (string.Join(' ', parts), Fingerprint(parts[1] + " " + parts[2])))
            .ToList();
    }

    private sealed record MetaResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("ssh_keys")] List<string>? SshKeys);
}
