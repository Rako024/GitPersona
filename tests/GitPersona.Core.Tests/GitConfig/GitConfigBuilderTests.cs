using GitPersona.Core.GitConfig;
using GitPersona.Core.Infrastructure;

namespace GitPersona.Core.Tests.GitConfig;

public class GitConfigBuilderTests
{
    private static readonly UserPaths Paths = new(@"C:\Users\me");

    [Theory]
    [InlineData("Mona Octocat", "Mona Octocat")]
    [InlineData("Gülər Şəkərli", "Gülər Şəkərli")]
    [InlineData("me@example.com", "me@example.com")]
    [InlineData(" padded", "\" padded\"")]
    [InlineData("a#b", "\"a#b\"")]
    [InlineData("semi;colon", "\"semi;colon\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\path", "\"C:\\\\path\"")]
    [InlineData("", "\"\"")]
    public void Values_are_quoted_only_when_needed(string value, string expected)
    {
        Assert.Equal(expected, GitConfigSyntax.Value(value));
    }

    [Fact]
    public void Line_breaks_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => GitConfigSyntax.Value("a\nb"));
    }

    [Theory]
    [InlineData(@"C:\Users\me\work", "gitdir/i:C:/Users/me/work/")]
    [InlineData(@"C:\Users\me\work\", "gitdir/i:C:/Users/me/work/")]
    [InlineData(@"D:\My Repos\old[2024]", @"gitdir/i:D:/My Repos/old\[2024\]/")]
    public void Folder_conditions_use_forward_slashes_a_trailing_slash_and_literal_brackets(string folder, string expected)
    {
        Assert.Equal(expected, GitConfigSyntax.GitDirCondition(folder));
    }

    [Fact]
    public void Glob_escapes_survive_subsection_quoting()
    {
        Assert.Equal("\"gitdir/i:D:/old\\\\[1\\\\]/\"", GitConfigSyntax.Subsection(GitConfigSyntax.GitDirCondition(@"D:\old[1]")));
    }

    [Fact]
    public void Global_block_orders_shallow_folders_first_and_alias_rules_last()
    {
        var personal = TestProfiles.Create("personal", Paths.HomeDirectory, @"C:\repos");
        var work = TestProfiles.Create("work", Paths.HomeDirectory, @"C:\repos\work");

        var lines = GitConfigBuilder.GlobalBlock([work, personal], Paths);

        Assert.Equal(
        [
            "# Identity by folder (deeper folders win)",
            "[includeIf \"gitdir/i:C:/repos/\"]",
            "\tpath = C:/Users/me/.gitconfig-personal",
            "[includeIf \"gitdir/i:C:/repos/work/\"]",
            "\tpath = C:/Users/me/.gitconfig-work",
            "# Identity by remote SSH alias (wins over folders)",
            "[includeIf \"hasconfig:remote.*.url:git@github-work:*/**\"]",
            "\tpath = C:/Users/me/.gitconfig-work",
            "[includeIf \"hasconfig:remote.*.url:ssh://git@github-work/**\"]",
            "\tpath = C:/Users/me/.gitconfig-work",
            "[includeIf \"hasconfig:remote.*.url:git@github-personal:*/**\"]",
            "\tpath = C:/Users/me/.gitconfig-personal",
            "[includeIf \"hasconfig:remote.*.url:ssh://git@github-personal/**\"]",
            "\tpath = C:/Users/me/.gitconfig-personal",
        ], lines);
    }

    [Fact]
    public void Profile_file_sets_identity_and_the_https_username()
    {
        var profile = TestProfiles.Create("work", Paths.HomeDirectory) with { GitUserName = "Gülər Ş." };

        var text = GitConfigBuilder.ProfileFile(profile);

        Assert.True(GitConfigBuilder.IsManagedProfileFile(text));
        Assert.Equal(
            "# Managed by GitPersona for profile \"work\". Edit the profile in GitPersona; changes made here are overwritten.\n" +
            "[user]\n\tname = Gülər Ş.\n\temail = work@example.com\n" +
            "[github]\n\tuser = gh-work\n" +
            "[credential \"https://github.com\"]\n\tusername = gh-work\n",
            text);
    }

    [Fact]
    public void Profile_file_without_github_username_only_sets_identity()
    {
        var text = GitConfigBuilder.ProfileFile(TestProfiles.Create("x", Paths.HomeDirectory) with { GitHubUsername = "" });

        Assert.DoesNotContain("[github]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", text, StringComparison.Ordinal);
    }
}
