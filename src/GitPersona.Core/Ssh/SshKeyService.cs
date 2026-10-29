using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using Microsoft.Extensions.Logging;

namespace GitPersona.Core.Ssh;

public class SshKeyException(string message) : Exception(message);

public sealed class SshKeyExistsException(string path)
    : SshKeyException($"'{path}' already exists. GitPersona never overwrites keys.")
{
    public string Path { get; } = path;
}

/// <param name="PublicKey">The <c>.pub</c> line: <c>ssh-ed25519 AAAA... comment</c>.</param>
/// <param name="Fingerprint"><c>SHA256:...</c>, or <see langword="null"/> if ssh-keygen could not compute it.</param>
public sealed record SshKeyInfo(string PrivateKeyPath, string PublicKey, string KeyType, string Comment, string? Fingerprint);

/// <summary>Generates and inspects keys with ssh-keygen. Private keys stay where they are; nothing is copied.</summary>
public sealed class SshKeyService(IProcessRunner runner, ToolLocator tools, UserPaths paths, ILogger<SshKeyService> logger)
{
    private static readonly TimeSpan KeygenTimeout = TimeSpan.FromSeconds(30);

    /// <exception cref="SshKeyExistsException">The private or public key file already exists.</exception>
    /// <exception cref="SshKeyException">ssh-keygen failed.</exception>
    public async Task<SshKeyInfo> GenerateAsync(string privateKeyPath, string comment, string passphrase, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPath);
        ArgumentNullException.ThrowIfNull(passphrase);
        var fullPath = Path.GetFullPath(privateKeyPath);
        foreach (var existing in new[] { fullPath, fullPath + ".pub" })
        {
            if (File.Exists(existing))
            {
                throw new SshKeyExistsException(existing);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var request = new ProcessRequest(tools.RequireSshKeygen(), ["-t", "ed25519", "-C", comment, "-f", fullPath, "-N", passphrase])
        {
            Timeout = KeygenTimeout,
            SecretsToMask = [passphrase],
        };
        var result = await runner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || !File.Exists(fullPath + ".pub"))
        {
            throw new SshKeyException($"ssh-keygen failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }

        logger.LogInformation("Generated ed25519 key {Path} (passphrase: {HasPassphrase})", fullPath, passphrase.Length > 0);
        return await DescribeAsync(fullPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Key pairs in <c>~/.ssh</c> (private key files that have a matching <c>.pub</c>).</summary>
    public async Task<IReadOnlyList<SshKeyInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(paths.SshDirectory))
        {
            return [];
        }

        var keys = new List<SshKeyInfo>();
        foreach (var publicFile in Directory.EnumerateFiles(paths.SshDirectory, "*.pub"))
        {
            var privateFile = publicFile[..^".pub".Length];
            if (File.Exists(privateFile))
            {
                keys.Add(await DescribeAsync(privateFile, cancellationToken).ConfigureAwait(false));
            }
        }

        return keys.OrderBy(k => k.PrivateKeyPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <exception cref="SshKeyException">No public key could be found or derived.</exception>
    public async Task<SshKeyInfo> DescribeAsync(string privateKeyPath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(privateKeyPath);
        var publicKey = await ReadPublicKeyAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var parts = publicKey.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new SshKeyException($"'{fullPath}.pub' is not an OpenSSH public key.");
        }

        return new SshKeyInfo(
            fullPath,
            publicKey,
            parts[0],
            parts.Length > 2 ? parts[2] : string.Empty,
            await FingerprintAsync(fullPath, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Whether the key needs a passphrase (and therefore an agent for unattended use).
    /// Tries to read it with an empty passphrase, which never prompts.
    /// </summary>
    public async Task<bool> HasPassphraseAsync(string privateKeyPath, CancellationToken cancellationToken = default)
    {
        var request = new ProcessRequest(tools.RequireSshKeygen(), ["-y", "-P", string.Empty, "-f", Path.GetFullPath(privateKeyPath)])
        {
            Timeout = KeygenTimeout,
        };
        var result = await runner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        return !result.Succeeded;
    }

    private async Task<string> ReadPublicKeyAsync(string privateKeyPath, CancellationToken cancellationToken)
    {
        var publicFile = privateKeyPath + ".pub";
        if (File.Exists(publicFile))
        {
            return (await File.ReadAllTextAsync(publicFile, cancellationToken).ConfigureAwait(false)).Trim();
        }

        // No .pub next to it: derive it, which only works without a passphrase.
        var request = new ProcessRequest(tools.RequireSshKeygen(), ["-y", "-P", string.Empty, "-f", privateKeyPath]) { Timeout = KeygenTimeout };
        var result = await runner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new SshKeyException($"'{publicFile}' is missing and the public key cannot be derived without the passphrase.");
        }

        return result.StandardOutput.Trim();
    }

    private async Task<string?> FingerprintAsync(string privateKeyPath, CancellationToken cancellationToken)
    {
        var target = File.Exists(privateKeyPath + ".pub") ? privateKeyPath + ".pub" : privateKeyPath;
        var result = await runner.RunAsync(
            new ProcessRequest(tools.RequireSshKeygen(), ["-l", "-E", "sha256", "-f", target]) { Timeout = KeygenTimeout },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // "256 SHA256:abc... comment (ED25519)"
        return result.Succeeded
            ? result.StandardOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(p => p.StartsWith("SHA256:", StringComparison.Ordinal))
            : null;
    }
}
