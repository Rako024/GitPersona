using System.Text.RegularExpressions;
using GitPersona.Core.ConfigFiles;
using GitPersona.Core.Models;

namespace GitPersona.Core.Ssh;

/// <summary>Builds GitPersona's part of <c>~/.ssh/config</c>: one <c>Host</c> block per profile alias.</summary>
public static partial class SshConfigBuilder
{
    [GeneratedRegex(@"^\s*Host(?:\s+|\s*=\s*)(?<patterns>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostLine();

    /// <summary>Profiles without an SSH key get no block: ssh would fall back to whatever default key it finds.</summary>
    public static IReadOnlyList<string> Block(IReadOnlyList<Profile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var lines = new List<string>();
        foreach (var profile in profiles.Where(p => !string.IsNullOrWhiteSpace(p.SshKeyPath) && !string.IsNullOrWhiteSpace(p.HostAlias)))
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add($"# {profile.Id}: {SingleLine(profile.DisplayName)}");
            lines.Add($"Host {profile.HostAlias}");
            lines.Add($"    HostName {profile.Host}");
            lines.Add("    User git");
            lines.Add($"    IdentityFile {Quote(ToSshPath(profile.SshKeyPath))}");
            // Offer only this key; otherwise ssh may authenticate with another account's key first.
            lines.Add("    IdentitiesOnly yes");
        }

        return lines;
    }

    /// <summary>Aliases that also appear on a <c>Host</c> line outside GitPersona's block.</summary>
    public static IReadOnlyList<string> AliasesDefinedElsewhere(string configText, IEnumerable<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(configText);
        var outside = ManagedBlock.Remove(configText);
        var patterns = outside.Split('\n')
            .Select(line => HostLine().Match(line.TrimEnd('\r')))
            .Where(match => match.Success)
            .SelectMany(match => match.Groups["patterns"].Value.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries))
            .Select(pattern => pattern.Trim('"'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return aliases.Where(patterns.Contains).ToList();
    }

    /// <summary>Forward slashes work for both Windows OpenSSH and Git's bundled ssh.</summary>
    public static string ToSshPath(string path) => Path.GetFullPath(path).Replace('\\', '/');

    private static string Quote(string value) => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static string SingleLine(string text) =>
        string.Concat(text.Select(c => char.IsControl(c) ? ' ' : c));
}
