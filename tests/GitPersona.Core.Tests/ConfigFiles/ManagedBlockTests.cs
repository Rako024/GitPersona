using GitPersona.Core.ConfigFiles;

namespace GitPersona.Core.Tests.ConfigFiles;

public class ManagedBlockTests
{
    private static readonly string[] Body = ["Host github-work", "    HostName github.com"];

    private static string Block(string newline = "\n") =>
        string.Join(newline, ManagedBlock.StartMarker, ManagedBlock.Notice, Body[0], Body[1], ManagedBlock.EndMarker) + newline;

    [Fact]
    public void Upsert_into_empty_text_returns_only_the_block()
    {
        Assert.Equal(Block(), ManagedBlock.Upsert(string.Empty, Body, BlockPlacement.Top));
    }

    [Fact]
    public void Upsert_at_top_keeps_existing_content_untouched_after_the_block()
    {
        const string existing = "Host *\n    ServerAliveInterval 60\n";

        var result = ManagedBlock.Upsert(existing, Body, BlockPlacement.Top);

        Assert.Equal(Block() + existing, result);
    }

    [Fact]
    public void Upsert_at_bottom_appends_after_existing_content()
    {
        const string existing = "[user]\n\temail = me@example.com\n";

        var result = ManagedBlock.Upsert(existing, Body, BlockPlacement.Bottom);

        Assert.Equal(existing + Block(), result);
    }

    [Fact]
    public void Upsert_at_bottom_terminates_a_last_line_without_line_break()
    {
        var result = ManagedBlock.Upsert("[user]", Body, BlockPlacement.Bottom);

        Assert.Equal("[user]\n" + Block(), result);
    }

    [Fact]
    public void Upsert_replaces_an_existing_block_in_place_and_preserves_both_sides()
    {
        const string before = "# mine\nHost a\n";
        const string after = "Host z\n    User me\n";
        var original = before + ManagedBlock.StartMarker + "\nold line\n" + ManagedBlock.EndMarker + "\n" + after;

        var result = ManagedBlock.Upsert(original, Body, BlockPlacement.Bottom);

        Assert.Equal(before + Block() + after, result);
    }

    [Fact]
    public void Upsert_uses_the_files_crlf_line_endings()
    {
        const string existing = "Host *\r\n    User git\r\n";

        var result = ManagedBlock.Upsert(existing, Body, BlockPlacement.Top);

        Assert.Equal(Block("\r\n") + existing, result);
    }

    [Fact]
    public void Upsert_is_idempotent()
    {
        var once = ManagedBlock.Upsert("Host *\n", Body, BlockPlacement.Top);

        Assert.Equal(once, ManagedBlock.Upsert(once, Body, BlockPlacement.Top));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Host *\n    User git\n")]
    [InlineData("Host *\r\n    User git\r\n")]
    [InlineData("﻿[core]\n\tautocrlf = true\n\n\n")]
    public void Remove_is_the_exact_inverse_of_inserting(string original)
    {
        foreach (var placement in Enum.GetValues<BlockPlacement>())
        {
            var withBlock = ManagedBlock.Upsert(original, Body, placement);

            Assert.Equal(original, ManagedBlock.Remove(withBlock));
        }
    }

    [Fact]
    public void Markers_are_found_despite_surrounding_whitespace()
    {
        var text = "  " + ManagedBlock.StartMarker + "  \nx\n\t" + ManagedBlock.EndMarker + "\n";

        Assert.Equal(new ManagedBlock.Range(0, text.Length), ManagedBlock.Find(text));
    }

    [Fact]
    public void ReadBody_returns_lines_between_markers_without_the_notice()
    {
        var text = "keep\n" + Block() + "keep\n";

        Assert.Equal(Body, ManagedBlock.ReadBody(text));
    }

    [Fact]
    public void ReadBody_returns_null_without_a_block()
    {
        Assert.Null(ManagedBlock.ReadBody("Host *\n"));
    }

    [Theory]
    [InlineData("# >>> GitPersona managed >>>\nx\n")]
    [InlineData("x\n# <<< GitPersona managed <<<\n")]
    [InlineData("# <<< GitPersona managed <<<\n# >>> GitPersona managed >>>\n")]
    [InlineData("# >>> GitPersona managed >>>\n# >>> GitPersona managed >>>\n# <<< GitPersona managed <<<\n")]
    [InlineData("# >>> GitPersona managed >>>\n# <<< GitPersona managed <<<\n# >>> GitPersona managed >>>\n# <<< GitPersona managed <<<\n")]
    public void Malformed_markers_are_rejected_instead_of_guessed(string text)
    {
        Assert.Throws<MalformedManagedBlockException>(() => ManagedBlock.Upsert(text, Body, BlockPlacement.Top));
    }

    [Theory]
    [InlineData("# >>> GitPersona managed >>>")]
    [InlineData("a\nb")]
    public void Body_lines_that_would_corrupt_the_block_are_rejected(string line)
    {
        Assert.Throws<ArgumentException>(() => ManagedBlock.Upsert(string.Empty, [line], BlockPlacement.Top));
    }
}
