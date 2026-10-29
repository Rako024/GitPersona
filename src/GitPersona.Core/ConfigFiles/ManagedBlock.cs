namespace GitPersona.Core.ConfigFiles;

public enum BlockPlacement
{
    /// <summary>Start of the file: ssh uses the first value it finds, so our host blocks must come first.</summary>
    Top,

    /// <summary>End of the file: git uses the last value it finds, so our includes must come last.</summary>
    Bottom,
}

public sealed class MalformedManagedBlockException(string message) : Exception(message);

/// <summary>
/// Reads and rewrites the single region of a config file that GitPersona owns:
/// <code>
/// # &gt;&gt;&gt; GitPersona managed &gt;&gt;&gt;
/// ...
/// # &lt;&lt;&lt; GitPersona managed &lt;&lt;&lt;
/// </code>
/// Everything outside the markers is preserved exactly, including line endings.
/// </summary>
public static class ManagedBlock
{
    public const string StartMarker = "# >>> GitPersona managed >>>";
    public const string EndMarker = "# <<< GitPersona managed <<<";
    public const string Notice = "# Do not edit by hand: GitPersona rewrites everything between these markers.";

    /// <summary>
    /// Character range of the block: from the start of the start-marker line up to and including
    /// the line break that ends the end-marker line.
    /// </summary>
    public readonly record struct Range(int Start, int End);

    /// <exception cref="MalformedManagedBlockException">Markers are duplicated, unmatched or out of order.</exception>
    public static Range? Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Range? found = null;
        int? start = null;

        foreach (var line in EnumerateLines(text))
        {
            var content = text.AsSpan(line.Start, line.ContentEnd - line.Start).Trim();
            if (content.SequenceEqual(StartMarker))
            {
                if (start is not null || found is not null)
                {
                    throw new MalformedManagedBlockException("The file contains more than one GitPersona start marker.");
                }

                start = line.Start;
            }
            else if (content.SequenceEqual(EndMarker))
            {
                if (start is null)
                {
                    throw new MalformedManagedBlockException("The file contains a GitPersona end marker without a start marker.");
                }

                found = new Range(start.Value, line.Next);
                start = null;
            }
        }

        if (start is not null)
        {
            throw new MalformedManagedBlockException("The GitPersona start marker has no matching end marker.");
        }

        return found;
    }

    /// <summary>Lines between the markers (excluding the notice), or <see langword="null"/> when there is no block.</summary>
    public static IReadOnlyList<string>? ReadBody(string text)
    {
        if (Find(text) is not { } range)
        {
            return null;
        }

        return EnumerateLines(text)
            .Where(line => line.Start >= range.Start && line.Next <= range.End)
            .Select(line => text[line.Start..line.ContentEnd])
            .Skip(1)
            .SkipLast(1)
            .Where(line => line.Trim() != Notice)
            .ToList();
    }

    /// <summary>
    /// Replaces the existing block in place, or inserts a new one at <paramref name="placement"/>.
    /// An existing block is never moved, so a user who relocated it keeps their layout.
    /// </summary>
    public static string Upsert(string text, IEnumerable<string> bodyLines, BlockPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(text);
        var newline = DetectNewline(text);
        var block = Render(bodyLines, newline);

        if (Find(text) is { } range)
        {
            return string.Concat(text.AsSpan(0, range.Start), block, text.AsSpan(range.End));
        }

        if (text.Length == 0)
        {
            return block;
        }

        return placement == BlockPlacement.Top
            ? block + text
            : text + (text.EndsWith('\n') ? string.Empty : newline) + block;
    }

    public static string Remove(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Find(text) is { } range ? text.Remove(range.Start, range.End - range.Start) : text;
    }

    private static string Render(IEnumerable<string> bodyLines, string newline)
    {
        ArgumentNullException.ThrowIfNull(bodyLines);
        var lines = new List<string> { StartMarker, Notice };
        foreach (var line in bodyLines)
        {
            if (line.Contains('\n') || line.Contains('\r'))
            {
                throw new ArgumentException("Block lines must not contain line breaks.", nameof(bodyLines));
            }

            if (line.Trim() is StartMarker or EndMarker)
            {
                throw new ArgumentException("Block lines must not contain GitPersona markers.", nameof(bodyLines));
            }

            lines.Add(line);
        }

        lines.Add(EndMarker);
        return string.Join(newline, lines) + newline;
    }

    /// <summary>The file's first line break style, defaulting to LF (what git itself writes).</summary>
    internal static string DetectNewline(string text)
    {
        var index = text.IndexOf('\n', StringComparison.Ordinal);
        return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
    }

    private readonly record struct Line(int Start, int ContentEnd, int Next);

    private static IEnumerable<Line> EnumerateLines(string text)
    {
        var position = 0;
        while (position < text.Length)
        {
            var lineBreak = text.IndexOf('\n', position);
            if (lineBreak < 0)
            {
                yield return new Line(position, text.Length, text.Length);
                yield break;
            }

            var contentEnd = lineBreak > position && text[lineBreak - 1] == '\r' ? lineBreak - 1 : lineBreak;
            yield return new Line(position, contentEnd, lineBreak + 1);
            position = lineBreak + 1;
        }
    }
}
