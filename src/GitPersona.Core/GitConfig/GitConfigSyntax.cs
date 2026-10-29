using System.Text.RegularExpressions;
using GitPersona.Core.Infrastructure;

namespace GitPersona.Core.GitConfig;

/// <summary>Quoting rules of git's config file format.</summary>
public static partial class GitConfigSyntax
{
    [GeneratedRegex(@"[\\*?\[\]]", RegexOptions.CultureInvariant)]
    private static partial Regex GlobMetacharacters();

    /// <summary>A value, quoted only when git would otherwise change its meaning.</summary>
    public static string Value(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        RejectLineBreaks(value);
        var needsQuotes = value.Length == 0
            || char.IsWhiteSpace(value[0])
            || char.IsWhiteSpace(value[^1])
            || value.AsSpan().IndexOfAny("\"\\#;") >= 0;
        return needsQuotes ? Quote(value) : value;
    }

    /// <summary>A subsection name, e.g. the condition in <c>[includeIf "..."]</c>; always quoted.</summary>
    public static string Subsection(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        RejectLineBreaks(name);
        return Quote(name);
    }

    /// <summary><c>C:\Users\me\work</c> → <c>C:/Users/me/work</c>, the form git uses on Windows.</summary>
    public static string ToGitPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Replace('\\', '/');

    /// <summary>Escapes wildmatch metacharacters so a folder name like <c>repos[old]</c> is matched literally.</summary>
    public static string EscapeGlob(string text) => GlobMetacharacters().Replace(text, @"\$0");

    /// <summary>
    /// <c>gitdir/i:C:/Users/me/work/</c> — case-insensitive (Windows paths) and, thanks to the trailing slash,
    /// matching every repository below the folder.
    /// </summary>
    /// <remarks>
    /// The folder is written in its long form: git also matches against the gitdir's real path, which Git for
    /// Windows reports with long names, so a pattern containing 8.3 short names (<c>MONAOC~1</c>) could fail.
    /// </remarks>
    public static string GitDirCondition(string folder) => $"gitdir/i:{EscapeGlob(ToGitPath(LongPath.Expand(folder)))}/";

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static void RejectLineBreaks(string text)
    {
        if (text.Contains('\n') || text.Contains('\r'))
        {
            throw new ArgumentException("Git config values cannot contain line breaks.", nameof(text));
        }
    }
}
