using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Ssh;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.Integration;

/// <summary>Uses the real ssh-keygen, writing only into a temporary .ssh folder.</summary>
public sealed class SshKeyServiceTests : IDisposable
{
    private readonly TempDirectory _home = new();
    private readonly UserPaths _paths;
    private readonly SshKeyService _keys;

    public SshKeyServiceTests()
    {
        _paths = new UserPaths(_home.Path);
        _keys = new SshKeyService(new ProcessRunner(NullLogger<ProcessRunner>.Instance), new ToolLocator(), _paths, NullLogger<SshKeyService>.Instance);
    }

    public void Dispose() => _home.Dispose();

    [Fact]
    public async Task Generates_an_ed25519_key_pair_without_passphrase()
    {
        var path = _paths.DefaultSshKeyFile("work");

        var key = await _keys.GenerateAsync(path, "work@example.com", passphrase: "");

        Assert.True(File.Exists(path));
        Assert.Equal("ssh-ed25519", key.KeyType);
        Assert.Equal("work@example.com", key.Comment);
        Assert.StartsWith("SHA256:", key.Fingerprint, StringComparison.Ordinal);
        Assert.False(await _keys.HasPassphraseAsync(path));
    }

    [Fact]
    public async Task Generates_a_key_with_a_passphrase()
    {
        var path = _paths.DefaultSshKeyFile("secure");

        await _keys.GenerateAsync(path, "secure@example.com", passphrase: "correct horse battery staple");

        Assert.True(await _keys.HasPassphraseAsync(path));
    }

    [Fact]
    public async Task Never_overwrites_an_existing_key()
    {
        var path = _paths.DefaultSshKeyFile("work");
        Directory.CreateDirectory(_paths.SshDirectory);
        File.WriteAllText(path, "precious");

        await Assert.ThrowsAsync<SshKeyExistsException>(() => _keys.GenerateAsync(path, "x", ""));
        Assert.Equal("precious", File.ReadAllText(path));
    }

    [Fact]
    public async Task Lists_key_pairs_and_derives_a_missing_public_key()
    {
        var first = await _keys.GenerateAsync(_paths.DefaultSshKeyFile("a"), "a@example.com", "");
        await _keys.GenerateAsync(_paths.DefaultSshKeyFile("b"), "b@example.com", "");
        File.WriteAllText(Path.Combine(_paths.SshDirectory, "known_hosts"), "github.com ssh-ed25519 AAAA");

        var listed = await _keys.ListAsync();
        File.Delete(first.PrivateKeyPath + ".pub");
        var derived = await _keys.DescribeAsync(first.PrivateKeyPath);

        Assert.Equal(["id_ed25519_a", "id_ed25519_b"], listed.Select(k => Path.GetFileName(k.PrivateKeyPath)));
        Assert.Equal(first.PublicKey.Split(' ')[1], derived.PublicKey.Split(' ')[1]);
        Assert.Equal(first.Fingerprint, derived.Fingerprint);
    }
}
