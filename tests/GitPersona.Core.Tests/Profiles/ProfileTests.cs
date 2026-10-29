using GitPersona.Core.Infrastructure;
using GitPersona.Core.Models;
using GitPersona.Core.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.Profiles;

public sealed class ProfileTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static Profile Valid(string id, string? alias = null) => new()
    {
        Id = id,
        DisplayName = $"Profile {id}",
        GitUserName = "Gülər Şəkərli",
        GitUserEmail = $"{id}@example.com",
        GitHubUsername = id,
        HostAlias = alias ?? Profile.DefaultAliasFor(id),
        Folders = [$@"C:\repos\{id}"],
        Owners = [$"{id}-org"],
    };

    [Fact]
    public void Valid_profiles_have_no_issues()
    {
        Assert.Empty(ProfileValidator.Validate([Valid("work"), Valid("personal")]));
    }

    [Theory]
    [InlineData("Work")]
    [InlineData("-work")]
    [InlineData("work-")]
    [InlineData("my work")]
    [InlineData("")]
    public void Ids_must_be_lowercase_slugs(string id)
    {
        var issue = Assert.Single(ProfileValidator.Validate([Valid("x") with { Id = id }]), i => i.Code == ProfileIssueCode.InvalidId);
        Assert.Equal(id, issue.Value);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a b@example.com")]
    [InlineData("")]
    public void Emails_must_be_valid(string email)
    {
        Assert.Contains(
            ProfileValidator.Validate([Valid("work") with { GitUserEmail = email }]),
            i => i.Code == ProfileIssueCode.InvalidEmail);
    }

    [Theory]
    [InlineData("github-*")]
    [InlineData("github work")]
    [InlineData("")]
    public void Host_aliases_must_be_plain_names(string alias)
    {
        Assert.Contains(
            ProfileValidator.Validate([Valid("work", alias)]),
            i => i.Code == ProfileIssueCode.InvalidHostAlias);
    }

    [Fact]
    public void Folders_must_be_absolute()
    {
        Assert.Contains(
            ProfileValidator.Validate([Valid("work") with { Folders = [@"repos\work"] }]),
            i => i.Code == ProfileIssueCode.InvalidFolder);
    }

    [Fact]
    public void The_same_folder_owner_or_alias_cannot_belong_to_two_profiles()
    {
        var work = Valid("work") with { Folders = [@"C:\Repos\Shared\"], Owners = ["Acme"] };
        var personal = Valid("personal", alias: "GITHUB-WORK") with { Folders = [@"c:\repos\shared"], Owners = ["acme"] };

        var codes = ProfileValidator.Validate([work, personal]).Select(i => i.Code).ToHashSet();

        Assert.True(
            codes.SetEquals([ProfileIssueCode.DuplicateHostAlias, ProfileIssueCode.DuplicateFolder, ProfileIssueCode.DuplicateOwner]),
            string.Join(", ", codes));
    }

    [Fact]
    public void Nested_folders_in_different_profiles_are_allowed()
    {
        var personal = Valid("personal") with { Folders = [@"C:\repos"] };
        var work = Valid("work") with { Folders = [@"C:\repos\work"] };

        Assert.Empty(ProfileValidator.Validate([personal, work]));
    }

    [Fact]
    public void Store_round_trips_profiles()
    {
        var store = new ProfileStore(new AppPaths(_temp.Path), NullLogger<ProfileStore>.Instance);
        var profiles = new[] { Valid("work"), Valid("personal") };

        store.Save(profiles);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Gülər Şəkərli", loaded[0].GitUserName);
        Assert.Equal(profiles[1].Folders, loaded[1].Folders);
        Assert.Contains("Gülər", File.ReadAllText(_temp.Combine("profiles.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Store_refuses_to_save_invalid_profiles()
    {
        var store = new ProfileStore(new AppPaths(_temp.Path), NullLogger<ProfileStore>.Instance);

        var ex = Assert.Throws<ProfileValidationException>(() => store.Save([Valid("work"), Valid("work")]));

        Assert.Contains(ex.Issues, i => i.Code == ProfileIssueCode.DuplicateId);
        Assert.False(File.Exists(_temp.Combine("profiles.json")));
    }

    [Fact]
    public void Store_moves_a_corrupt_file_aside_instead_of_losing_it()
    {
        var paths = new AppPaths(_temp.Path);
        File.WriteAllText(paths.ProfilesFile, "{ not json");
        var store = new ProfileStore(paths, NullLogger<ProfileStore>.Instance);

        Assert.Empty(store.Load());

        Assert.NotNull(store.QuarantinedFile);
        Assert.Equal("{ not json", File.ReadAllText(store.QuarantinedFile));
        Assert.False(File.Exists(paths.ProfilesFile));
    }
}
