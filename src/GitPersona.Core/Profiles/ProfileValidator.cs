using System.Net.Mail;
using System.Text.RegularExpressions;
using GitPersona.Core.Models;

namespace GitPersona.Core.Profiles;

public enum ProfileIssueCode
{
    InvalidId,
    DuplicateId,
    MissingDisplayName,
    MissingGitUserName,
    InvalidEmail,
    MissingHost,
    InvalidHostAlias,
    DuplicateHostAlias,
    InvalidFolder,
    DuplicateFolder,
    DuplicateOwner,
}

/// <param name="Value">The offending value (folder, owner, alias, ...), when there is one.</param>
public sealed record ProfileIssue(string ProfileId, ProfileIssueCode Code, string? Value = null);

public sealed class ProfileValidationException(IReadOnlyList<ProfileIssue> issues)
    : Exception($"The profiles are not valid: {string.Join(", ", issues.Select(i => $"{i.ProfileId}:{i.Code}"))}")
{
    public IReadOnlyList<ProfileIssue> Issues { get; } = issues;
}

public static partial class ProfileValidator
{
    // Lower-case letters, digits and inner hyphens: safe in file names, ssh Host patterns and git config keys.
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    // ssh Host aliases must not contain wildcards, whitespace or characters git would treat as URL syntax.
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex AliasPattern();

    public static bool IsValidId(string id) => IdPattern().IsMatch(id);

    public static IReadOnlyList<ProfileIssue> Validate(IReadOnlyList<Profile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var issues = new List<ProfileIssue>();

        foreach (var profile in profiles)
        {
            if (!IsValidId(profile.Id))
            {
                issues.Add(new(profile.Id, ProfileIssueCode.InvalidId, profile.Id));
            }

            if (string.IsNullOrWhiteSpace(profile.DisplayName))
            {
                issues.Add(new(profile.Id, ProfileIssueCode.MissingDisplayName));
            }

            if (string.IsNullOrWhiteSpace(profile.GitUserName))
            {
                issues.Add(new(profile.Id, ProfileIssueCode.MissingGitUserName));
            }

            if (!IsValidEmail(profile.GitUserEmail))
            {
                issues.Add(new(profile.Id, ProfileIssueCode.InvalidEmail, profile.GitUserEmail));
            }

            if (string.IsNullOrWhiteSpace(profile.Host))
            {
                issues.Add(new(profile.Id, ProfileIssueCode.MissingHost));
            }

            if (!AliasPattern().IsMatch(profile.HostAlias))
            {
                issues.Add(new(profile.Id, ProfileIssueCode.InvalidHostAlias, profile.HostAlias));
            }

            issues.AddRange(profile.Folders
                .Where(folder => !Path.IsPathFullyQualified(folder))
                .Select(folder => new ProfileIssue(profile.Id, ProfileIssueCode.InvalidFolder, folder)));
        }

        AddDuplicates(issues, profiles, p => [p.Id], StringComparer.Ordinal, ProfileIssueCode.DuplicateId);
        AddDuplicates(issues, profiles, p => [p.HostAlias], StringComparer.OrdinalIgnoreCase, ProfileIssueCode.DuplicateHostAlias);
        AddDuplicates(
            issues,
            profiles,
            p => p.Folders.Where(Path.IsPathFullyQualified).Select(NormalizeFolder),
            StringComparer.OrdinalIgnoreCase,
            ProfileIssueCode.DuplicateFolder);
        AddDuplicates(issues, profiles, p => p.Owners, StringComparer.OrdinalIgnoreCase, ProfileIssueCode.DuplicateOwner);
        return issues;
    }

    /// <summary>Full path without a trailing separator, for comparing folders.</summary>
    public static string NormalizeFolder(string folder) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && !email.Any(char.IsWhiteSpace)
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email;

    private static void AddDuplicates(
        List<ProfileIssue> issues,
        IReadOnlyList<Profile> profiles,
        Func<Profile, IEnumerable<string>> values,
        StringComparer comparer,
        ProfileIssueCode code)
    {
        var duplicates = profiles
            .SelectMany(profile => values(profile)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(comparer)
                .Select(value => (profile.Id, value)))
            .GroupBy(x => x.value, comparer)
            .Where(group => group.Count() > 1);

        foreach (var group in duplicates)
        {
            issues.AddRange(group.Select(x => new ProfileIssue(x.Id, code, x.value)));
        }
    }
}
