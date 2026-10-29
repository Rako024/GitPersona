using GitPersona.Core.ConfigFiles;
using GitPersona.Core.Profiles;

namespace GitPersona.Core.Tests.ConfigFiles;

public class LineDiffTests
{
    private static string Render(IEnumerable<DiffLine> lines) => string.Join("|", lines.Select(l => l.Kind switch
    {
        DiffLineKind.Added => "+" + l.Text,
        DiffLineKind.Removed => "-" + l.Text,
        DiffLineKind.Skipped => $"~{l.SkippedCount}",
        _ => " " + l.Text,
    }));

    [Fact]
    public void A_new_file_is_all_additions()
    {
        Assert.Equal("+a|+b", Render(LineDiff.Compute(null, "a\nb\n")));
    }

    [Fact]
    public void A_deleted_file_is_all_removals()
    {
        Assert.Equal("-a|-b", Render(LineDiff.Compute("a\nb", null)));
    }

    [Fact]
    public void Changed_lines_show_with_context_and_far_lines_are_collapsed()
    {
        var before = "1\n2\n3\n4\n5\n6\n7\n8\n9\n";
        var after = "1\n2\n3\n4\n5\nfive\n7\n8\n9\n";

        Assert.Equal("~2| 3| 4| 5|-6|+five| 7| 8| 9", Render(LineDiff.Compute(before, after)));
    }

    [Fact]
    public void Crlf_and_lf_lines_compare_equal()
    {
        Assert.Equal("~2", Render(LineDiff.Compute("a\r\nb\r\n", "a\nb\n")));
    }

    [Theory]
    [InlineData("Work", "work")]
    [InlineData("İş — Şirkət", "is-sirket")]
    [InlineData("  Açıq Mənbə 2024!  ", "aciq-menbe-2024")]
    [InlineData("Ğüləş Çörək", "gules-corek")]
    [InlineData("***", "")]
    public void Display_names_become_valid_ids(string name, string expected)
    {
        var slug = ProfileIds.Slugify(name);

        Assert.Equal(expected, slug);
        Assert.True(slug.Length == 0 || ProfileValidator.IsValidId(slug));
    }

    [Fact]
    public void Long_names_are_truncated_to_a_valid_id()
    {
        var slug = ProfileIds.Slugify(new string('a', 31) + " b c d e f");

        Assert.True(ProfileValidator.IsValidId(slug), slug);
    }
}
