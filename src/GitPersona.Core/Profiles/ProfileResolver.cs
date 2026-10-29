using GitPersona.Core.Git;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Models;

namespace GitPersona.Core.Profiles;

public enum ProfileMatchReason
{
    /// <summary>The URL already uses the profile's SSH alias.</summary>
    Alias,

    /// <summary>The repository owner is listed in the profile's owners.</summary>
    Owner,

    /// <summary>The repository belongs to the profile's own GitHub account.</summary>
    Username,
}

public sealed record ProfileMatch(Profile Profile, ProfileMatchReason Reason);

/// <summary>Decides which profile a repository URL or a folder belongs to.</summary>
public static class ProfileResolver
{
    public static ProfileMatch? ForRepository(IReadOnlyList<Profile> profiles, RepoRef repository)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(repository);

        if (profiles.FirstOrDefault(p => p.HostAlias.Equals(repository.Host, StringComparison.OrdinalIgnoreCase)) is { } byAlias)
        {
            return new ProfileMatch(byAlias, ProfileMatchReason.Alias);
        }

        var sameHost = profiles.Where(p => p.Host.Equals(repository.Host, StringComparison.OrdinalIgnoreCase)).ToList();
        if (sameHost.FirstOrDefault(p => p.Owners.Contains(repository.Owner, StringComparer.OrdinalIgnoreCase)) is { } byOwner)
        {
            return new ProfileMatch(byOwner, ProfileMatchReason.Owner);
        }

        return sameHost.FirstOrDefault(p => p.GitHubUsername.Equals(repository.Owner, StringComparison.OrdinalIgnoreCase)) is { } byUser
            ? new ProfileMatch(byUser, ProfileMatchReason.Username)
            : null;
    }

    /// <summary>The profile whose folder contains <paramref name="path"/>; the deepest folder wins, as in git's includeIf order.</summary>
    /// <remarks>Both sides are compared in long-name form, so 8.3 short names on either side still match.</remarks>
    public static Profile? ForFolder(IReadOnlyList<Profile> profiles, string path)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var target = Normalize(path);
        return profiles
            .SelectMany(p => p.Folders.Where(Path.IsPathFullyQualified).Select(f => (profile: p, folder: Normalize(f))))
            .Where(x => target.StartsWith(x.folder, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.folder.Length)
            .Select(x => x.profile)
            .FirstOrDefault();
    }

    private static string Normalize(string folder) =>
        ProfileValidator.NormalizeFolder(LongPath.Expand(folder)) + Path.DirectorySeparatorChar;
}
