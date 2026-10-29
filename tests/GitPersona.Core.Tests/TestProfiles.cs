using GitPersona.Core.Models;

namespace GitPersona.Core.Tests;

internal static class TestProfiles
{
    public static Profile Create(string id, string home, params string[] folders) => new()
    {
        Id = id,
        DisplayName = $"Profile {id}",
        GitUserName = $"User {id}",
        GitUserEmail = $"{id}@example.com",
        GitHubUsername = $"gh-{id}",
        HostAlias = Profile.DefaultAliasFor(id),
        SshKeyPath = Path.Combine(home, ".ssh", $"id_ed25519_{id}"),
        Folders = folders,
        Owners = [$"{id}-org"],
    };
}
