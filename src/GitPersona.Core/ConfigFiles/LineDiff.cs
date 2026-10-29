namespace GitPersona.Core.ConfigFiles;

public enum DiffLineKind
{
    Unchanged,
    Added,
    Removed,

    /// <summary>Stands for a run of unchanged lines that is not shown; <see cref="DiffLine.Text"/> is empty.</summary>
    Skipped,
}

/// <param name="SkippedCount">Number of hidden lines for <see cref="DiffLineKind.Skipped"/>.</param>
public sealed record DiffLine(DiffLineKind Kind, string Text, int SkippedCount = 0);

/// <summary>A line-based diff for previewing config changes (files are small, so a plain LCS is enough).</summary>
public static class LineDiff
{
    public static IReadOnlyList<DiffLine> Compute(string? oldText, string? newText, int context = 3)
    {
        var a = SplitLines(oldText);
        var b = SplitLines(newText);

        // lcs[i, j] = length of the longest common subsequence of a[i..] and b[j..]
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var full = new List<DiffLine>(a.Length + b.Length);
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                full.Add(new DiffLine(DiffLineKind.Unchanged, a[x]));
                x++;
                y++;
            }
            else if (x < a.Length && (y == b.Length || lcs[x + 1, y] >= lcs[x, y + 1]))
            {
                // Removals before additions, as in unified diffs.
                full.Add(new DiffLine(DiffLineKind.Removed, a[x++]));
            }
            else
            {
                full.Add(new DiffLine(DiffLineKind.Added, b[y++]));
            }
        }

        return CollapseUnchanged(full, context);
    }

    private static List<DiffLine> CollapseUnchanged(List<DiffLine> lines, int context)
    {
        var keep = new bool[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == DiffLineKind.Unchanged)
            {
                continue;
            }

            for (var k = Math.Max(0, i - context); k <= Math.Min(lines.Count - 1, i + context); k++)
            {
                keep[k] = true;
            }
        }

        var result = new List<DiffLine>();
        var skipped = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            if (keep[i])
            {
                if (skipped > 0)
                {
                    result.Add(new DiffLine(DiffLineKind.Skipped, string.Empty, skipped));
                    skipped = 0;
                }

                result.Add(lines[i]);
            }
            else
            {
                skipped++;
            }
        }

        if (skipped > 0)
        {
            result.Add(new DiffLine(DiffLineKind.Skipped, string.Empty, skipped));
        }

        return result;
    }

    private static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return text.EndsWith('\n') ? lines[..^1] : lines;
    }
}
