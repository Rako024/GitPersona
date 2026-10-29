using GitPersona.Core.Git;
using GitPersona.Core.Models;

namespace GitPersona.Core.Scanning;

public sealed record RemoteInfo(string Name, string Url)
{
    public RepoRef? Repository { get; } = GitUrlParser.Parse(Url);

    public bool IsHttps => Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What git reports about one working copy; everything the analysis needs, nothing it computes.</summary>
/// <param name="EffectiveEmail">The user.email git would use (last value across all scopes and includes).</param>
/// <param name="LocalEmail">user.email set in the repository's own config, which overrides the profile's.</param>
/// <param name="LocalHooksPath">core.hooksPath set in the repository (husky v5+), which bypasses the commit guard.</param>
/// <param name="Error">git's message when it could not read the repository (e.g. "dubious ownership").</param>
public sealed record RepoFacts(
    string Path,
    IReadOnlyList<RemoteInfo> Remotes,
    string? EffectiveEmail,
    string? LocalEmail,
    string? LocalHooksPath,
    string? Error = null);

public enum RepoProfileSource
{
    Alias,
    Folder,
    Owner,
    Username,
}

public enum RepoIssueCode
{
    /// <summary>The remote uses https://; pushes go through Git Credential Manager instead of the profile's key.</summary>
    HttpsRemote,

    /// <summary>git@github.com:… — ssh picks whichever key it finds first, not necessarily the profile's.</summary>
    SshWithoutAlias,

    /// <summary>The remote uses another profile's alias than the folder's profile.</summary>
    OtherProfilesAlias,

    /// <summary>Commits would use a different email than the profile's.</summary>
    EmailMismatch,

    /// <summary>No user.email at all.</summary>
    EmailMissing,

    /// <summary>The repository has its own core.hooksPath, so the commit guard does not run there.</summary>
    OwnHooksPath,

    /// <summary>A GitHub remote that no profile claims.</summary>
    NoProfile,

    /// <summary>git could not read the repository.</summary>
    GitError,
}

public enum RepoIssueSeverity
{
    Problem,
    Note,
}

public enum RepoFixKind
{
    SetRemoteUrl,
    UnsetLocalEmail,
}

/// <param name="Remote">Remote name for <see cref="RepoFixKind.SetRemoteUrl"/>.</param>
public sealed record RepoFix(RepoFixKind Kind, string? Remote = null, string? NewUrl = null);

/// <param name="Current">The current value (remote URL or email) when it helps to show it.</param>
/// <param name="Expected">What it should be (alias URL or the profile's email).</param>
public sealed record RepoIssue(
    RepoIssueCode Code,
    RepoIssueSeverity Severity,
    string? Current = null,
    string? Expected = null,
    RepoFix? Fix = null);

public sealed record RepoReport(RepoFacts Facts, Profile? Profile, RepoProfileSource? Source, IReadOnlyList<RepoIssue> Issues)
{
    public string Name => System.IO.Path.GetFileName(Facts.Path);

    public bool HasProblems => Issues.Any(issue => issue.Severity == RepoIssueSeverity.Problem);

    /// <summary>The remote shown for the repository: origin, else the first one.</summary>
    public RemoteInfo? MainRemote =>
        Facts.Remotes.FirstOrDefault(r => r.Name == "origin") ?? (Facts.Remotes.Count > 0 ? Facts.Remotes[0] : null);
}
