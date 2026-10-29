using GitPersona.Core.ConfigFiles;
using GitPersona.Core.Ssh;

namespace GitPersona.Core.Tests.Ssh;

public class SshConfigBuilderTests
{
    [Fact]
    public void Each_profile_with_a_key_gets_a_host_block()
    {
        var work = TestProfiles.Create("work", @"C:\Users\me");
        var noKey = TestProfiles.Create("nokey", @"C:\Users\me") with { SshKeyPath = "" };

        var lines = SshConfigBuilder.Block([work, noKey]);

        Assert.Equal(
        [
            "# work: Profile work",
            "Host github-work",
            "    HostName github.com",
            "    User git",
            "    IdentityFile C:/Users/me/.ssh/id_ed25519_work",
            "    IdentitiesOnly yes",
        ], lines);
    }

    [Fact]
    public void Key_paths_with_spaces_are_quoted()
    {
        var profile = TestProfiles.Create("work", @"C:\Users\Ali Veli");

        Assert.Contains("    IdentityFile \"C:/Users/Ali Veli/.ssh/id_ed25519_work\"", SshConfigBuilder.Block([profile]));
    }

    [Fact]
    public void Blocks_are_separated_by_blank_lines()
    {
        var lines = SshConfigBuilder.Block([TestProfiles.Create("a", @"C:\h"), TestProfiles.Create("b", @"C:\h")]);

        Assert.Equal(string.Empty, lines[6]);
        Assert.Equal("Host github-b", lines[8]);
    }

    [Fact]
    public void Finds_aliases_the_user_also_defined_outside_the_block()
    {
        var text = ManagedBlock.Upsert("Host github-work other\n  User x\nhost=GITHUB-PERSONAL\nHost *\n", ["Host github-mine"], BlockPlacement.Top);

        var found = SshConfigBuilder.AliasesDefinedElsewhere(text, ["github-work", "github-personal", "github-mine"]);

        Assert.Equal(["github-work", "github-personal"], found);
    }
}
