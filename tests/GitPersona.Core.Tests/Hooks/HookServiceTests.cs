using GitPersona.Core.Hooks;
using GitPersona.Core.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace GitPersona.Core.Tests.Hooks;

public sealed class HookServiceTests : IDisposable
{
    internal static readonly HookMessages Messages = new(
        "GitPersona: commit bloklandı — it's the wrong identity",
        "Profile:", "Expected:", "Current:", "(not set)", "FIX-HINT", "BYPASS-HINT", "CONFLICT-NOTE");

    private readonly TempDirectory _temp = new();
    private readonly HookService _hooks;

    public HookServiceTests()
    {
        _hooks = new HookService(new AppPaths(_temp.Combine("app data")), NullLogger<HookService>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Installs_an_lf_only_stub_for_every_hook_and_the_dispatcher()
    {
        _hooks.Install([], Messages, previousHooksPath: null, guardEnabled: true);

        foreach (var name in HookService.HookNames)
        {
            var bytes = File.ReadAllBytes(Path.Combine(_hooks.HooksDirectory, name));
            Assert.Equal((byte)'#', bytes[0]);
            Assert.DoesNotContain((byte)'\r', bytes);
        }

        var dispatch = File.ReadAllBytes(Path.Combine(_hooks.HooksDirectory, "_gitpersona", "dispatch.sh"));
        Assert.DoesNotContain((byte)'\r', dispatch);
        Assert.False(dispatch.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "no BOM: sh would not see the shebang");
        Assert.True(_hooks.IsInstalled);
    }

    [Fact]
    public void Rules_list_aliases_and_normalized_folders()
    {
        var work = TestProfiles.Create("work", @"C:\h", @"D:\Work Repos\", @"relative\ignored") with { DisplayName = "İş" };

        var rules = HookService.BuildRules([work]);

        Assert.Equal(
            "alias\tgithub-work\twork@example.com\tİş (github-work)\n" +
            "folder\td:/work repos\twork@example.com\tİş (github-work)\n",
            rules);
    }

    [Fact]
    public void Long_path_expansion_keeps_parts_that_do_not_exist_yet()
    {
        var existing = LongPath.Expand(_temp.Path);

        Assert.DoesNotContain('~', existing);
        Assert.Equal(Path.Combine(existing, "not yet", "created"), LongPath.Expand(_temp.Combine("not yet", "created")));
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("tab\there", "'tab here'")]
    public void Settings_values_are_single_quoted_for_sh(string value, string expected)
    {
        Assert.Equal(expected, HookService.ShellQuote(value));
    }

    [Fact]
    public void Removing_keeps_files_that_are_not_GitPersonas()
    {
        _hooks.Install([], Messages, null, true);
        var mine = Path.Combine(_hooks.HooksDirectory, "pre-commit");
        File.WriteAllText(mine, "#!/bin/sh\necho custom\n");
        var extra = Path.Combine(_hooks.HooksDirectory, "notes.txt");
        File.WriteAllText(extra, "keep");

        _hooks.Remove();

        Assert.True(File.Exists(mine));
        Assert.True(File.Exists(extra));
        Assert.False(File.Exists(Path.Combine(_hooks.HooksDirectory, "commit-msg")));
        Assert.False(_hooks.IsInstalled);
    }

    [Fact]
    public void Removing_an_untouched_install_deletes_the_folder()
    {
        _hooks.Install([], Messages, null, true);

        _hooks.Remove();

        Assert.False(Directory.Exists(_hooks.HooksDirectory));
    }
}
