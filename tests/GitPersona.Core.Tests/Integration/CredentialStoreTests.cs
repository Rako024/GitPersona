using GitPersona.Core.Security;

namespace GitPersona.Core.Tests.Integration;

/// <summary>
/// Uses the real Windows Credential Manager with uniquely named entries that each test removes again.
/// </summary>
public sealed class CredentialStoreTests : IDisposable
{
    private readonly CredentialStore _store = new();
    private readonly string _prefix = $"GitPersona.Tests:{Guid.NewGuid():N}:";

    public void Dispose()
    {
        foreach (var entry in _store.Enumerate(_prefix + "*"))
        {
            _store.Delete(entry.TargetName);
        }
    }

    [Fact]
    public void Saves_reads_and_deletes_a_secret()
    {
        var target = _prefix + "token";

        _store.Save(target, "octocat", "gho_ŞəkillİSecret");
        var read = _store.Read(target);

        Assert.Equal(("octocat", "gho_ŞəkillİSecret"), read);
        Assert.True(_store.Delete(target));
        Assert.Null(_store.Read(target));
        Assert.False(_store.Delete(target));
    }

    [Fact]
    public void Saving_again_replaces_the_secret()
    {
        var target = _prefix + "token";

        _store.Save(target, "a", "first");
        _store.Save(target, "b", "second");

        Assert.Equal(("b", "second"), _store.Read(target));
    }

    [Fact]
    public void Enumerates_by_prefix_without_secrets()
    {
        _store.Save(_prefix + "one", "u1", "s1");
        _store.Save(_prefix + "two", "u2", "s2");

        var entries = _store.Enumerate(_prefix + "*").OrderBy(e => e.TargetName).ToList();

        Assert.Equal([_prefix + "one", _prefix + "two"], entries.Select(e => e.TargetName));
        Assert.Equal(["u1", "u2"], entries.Select(e => e.UserName));
        Assert.All(entries, e => Assert.True(DateTimeOffset.Now - e.LastWritten < TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Enumerating_a_prefix_with_no_entries_is_empty()
    {
        Assert.Empty(_store.Enumerate(_prefix + "nothing*"));
    }

    [Fact]
    public void Token_targets_are_normalized_per_host_and_login()
    {
        Assert.Equal("GitPersona:github.com:octocat", GitHubTokenStore.TargetFor(" GitHub.com ", "OctoCat"));
    }
}
