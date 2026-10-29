using System.Text.RegularExpressions;

namespace GitPersona.Core.Git;

/// <summary>A GitHub repository as written in a URL. <see cref="Host"/> may be a real host or an SSH alias.</summary>
public sealed record RepoRef(string Host, string Owner, string Name)
{
    public string FullName => $"{Owner}/{Name}";
}

/// <summary>
/// Understands the ways a GitHub repository is usually pasted:
/// <c>https://github.com/owner/repo[.git][/tree/...]</c>, <c>github.com/owner/repo</c>,
/// <c>git@host:owner/repo.git</c> (also with an alias as host), <c>ssh://git@host[:port]/owner/repo.git</c>
/// and the <c>owner/repo</c> shorthand for github.com.
/// </summary>
public static partial class GitUrlParser
{
    public const string DefaultHost = "github.com";

    // First path segments of github.com pages that are not repositories (github.com/orgs/acme, /settings/keys, ...).
    private static readonly HashSet<string> ReservedOwners = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "apps", "codespaces", "collections", "enterprise", "events", "explore", "features", "issues",
        "login", "marketplace", "new", "notifications", "orgs", "pricing", "pulls", "search", "settings",
        "sponsors", "topics", "trending", "users",
    };

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}$", RegexOptions.CultureInvariant)]
    private static partial Regex OwnerPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    // user@host:path — the host must be longer than one character so "C:\repos" stays a Windows path.
    [GeneratedRegex(@"^(?:(?<user>[^@/\s]+)@)?(?<host>[A-Za-z0-9][A-Za-z0-9._-]+):(?!//)(?<path>[^\s]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ScpLike();

    [GeneratedRegex(@"^(?<owner>[A-Za-z0-9-]+)/(?<name>[A-Za-z0-9._-]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Shorthand();

    public static RepoRef? Parse(string? input)
    {
        var text = input?.Trim().Trim('"', '\'', '<', '>');
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.StartsWith("git clone ", StringComparison.OrdinalIgnoreCase))
        {
            text = text["git clone ".Length..].Trim();
        }

        if (Shorthand().Match(text) is { Success: true } shorthand)
        {
            return Create(DefaultHost, shorthand.Groups["owner"].Value, shorthand.Groups["name"].Value);
        }

        if (!text.Contains("://", StringComparison.Ordinal) && ScpLike().Match(text) is { Success: true } scp)
        {
            return FromPath(scp.Groups["host"].Value, scp.Groups["path"].Value);
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            // "github.com/owner/repo" without a scheme; the first segment must look like a domain,
            // which also keeps Windows paths such as "C:/repos/api" out.
            var first = text.Split('/')[0];
            if (!first.Contains('.', StringComparison.Ordinal) || first.Contains(':', StringComparison.Ordinal))
            {
                return null;
            }

            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http" or "ssh" or "git")
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return FromPath(host, uri.AbsolutePath);
    }

    private static RepoRef? FromPath(string host, string path)
    {
        // Only the first two segments matter: /owner/repo/tree/main/src → owner, repo.
        var segments = Uri.UnescapeDataString(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? Create(host, segments[0], segments[1]) : null;
    }

    private static RepoRef? Create(string host, string owner, string name)
    {
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return OwnerPattern().IsMatch(owner) && !ReservedOwners.Contains(owner) && NamePattern().IsMatch(name) && name is not ("." or "..")
            ? new RepoRef(host.ToLowerInvariant(), owner, name)
            : null;
    }
}
