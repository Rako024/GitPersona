using GitPersona.Core.Git;
using GitPersona.Core.Profiles;

namespace GitPersona.Core.Tests.Git;

public class GitUrlParserTests
{
    [Theory]
    [InlineData("https://github.com/acme/api", "github.com", "acme", "api")]
    [InlineData("https://github.com/acme/api.git", "github.com", "acme", "api")]
    [InlineData("https://github.com/acme/api/", "github.com", "acme", "api")]
    [InlineData("https://github.com/acme/api/tree/main/src", "github.com", "acme", "api")]
    [InlineData("https://github.com/acme/api/pull/12#discussion", "github.com", "acme", "api")]
    [InlineData("https://www.github.com/acme/api", "github.com", "acme", "api")]
    [InlineData("https://someone@github.com/acme/api.git", "github.com", "acme", "api")]
    [InlineData("http://GitHub.com/Acme/Api", "github.com", "Acme", "Api")]
    [InlineData("github.com/acme/api", "github.com", "acme", "api")]
    [InlineData("git@github.com:acme/api.git", "github.com", "acme", "api")]
    [InlineData("git@github.com:acme/api", "github.com", "acme", "api")]
    [InlineData("git@github-work:acme/my.repo.git", "github-work", "acme", "my.repo")]
    [InlineData("github-work:acme/api.git", "github-work", "acme", "api")]
    [InlineData("ssh://git@github.com/acme/api.git", "github.com", "acme", "api")]
    [InlineData("ssh://git@ssh.github.com:443/acme/api.git", "ssh.github.com", "acme", "api")]
    [InlineData("git://github.com/acme/api.git", "github.com", "acme", "api")]
    [InlineData("acme/api", "github.com", "acme", "api")]
    [InlineData("  \"https://github.com/acme/api\"  ", "github.com", "acme", "api")]
    [InlineData("git clone git@github.com:acme/api.git", "github.com", "acme", "api")]
    [InlineData("https://git.acme.example/team/service", "git.acme.example", "team", "service")]
    public void Recognizes_the_usual_ways_a_repository_is_pasted(string input, string host, string owner, string name)
    {
        Assert.Equal(new RepoRef(host, owner, name), GitUrlParser.Parse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("https://github.com/acme")]
    [InlineData("https://github.com/")]
    [InlineData("https://github.com/orgs/acme/repositories")]
    [InlineData("https://github.com/settings/keys")]
    [InlineData(@"C:\repos\api")]
    [InlineData("C:/repos/api")]
    [InlineData("ftp://github.com/acme/api")]
    [InlineData("https://github.com/-bad/api")]
    [InlineData("https://github.com/acme/..")]
    public void Rejects_anything_that_is_not_a_repository(string? input)
    {
        Assert.Null(GitUrlParser.Parse(input));
    }

    [Fact]
    public void Resolves_by_alias_then_owner_then_username()
    {
        var work = TestProfiles.Create("work", @"C:\h") with { Owners = ["acme"], GitHubUsername = "me-at-work" };
        var personal = TestProfiles.Create("personal", @"C:\h") with { Owners = [], GitHubUsername = "me" };
        var profiles = new[] { work, personal };

        Assert.Equal(new ProfileMatch(personal, ProfileMatchReason.Alias), ProfileResolver.ForRepository(profiles, new RepoRef("github-personal", "acme", "x")));
        Assert.Equal(new ProfileMatch(work, ProfileMatchReason.Owner), ProfileResolver.ForRepository(profiles, new RepoRef("github.com", "ACME", "x")));
        Assert.Equal(new ProfileMatch(personal, ProfileMatchReason.Username), ProfileResolver.ForRepository(profiles, new RepoRef("github.com", "Me", "dotfiles")));
        Assert.Null(ProfileResolver.ForRepository(profiles, new RepoRef("github.com", "stranger", "x")));
    }

    [Fact]
    public void Owner_rules_only_apply_to_the_profiles_host()
    {
        var enterprise = TestProfiles.Create("corp", @"C:\h") with { Host = "git.corp.example", Owners = ["acme"] };

        Assert.Null(ProfileResolver.ForRepository([enterprise], new RepoRef("github.com", "acme", "x")));
        Assert.NotNull(ProfileResolver.ForRepository([enterprise], new RepoRef("git.corp.example", "acme", "x")));
    }

    [Fact]
    public void Folder_resolution_picks_the_deepest_folder_and_respects_name_boundaries()
    {
        var personal = TestProfiles.Create("personal", @"C:\h", @"C:\repos");
        var work = TestProfiles.Create("work", @"C:\h", @"C:\repos\work\");
        var profiles = new[] { personal, work };

        Assert.Equal(work, ProfileResolver.ForFolder(profiles, @"c:\REPOS\work\api"));
        Assert.Equal(work, ProfileResolver.ForFolder(profiles, @"C:\repos\work"));
        Assert.Equal(personal, ProfileResolver.ForFolder(profiles, @"C:\repos\workshop"));
        Assert.Null(ProfileResolver.ForFolder(profiles, @"C:\repositories\x"));
    }
}
