using GitPersona.Core.Git;
using GitPersona.Core.Models;
using GitPersona.Core.Profiles;

namespace GitPersona.Core.Scanning;

/// <summary>Turns <see cref="RepoFacts"/> into a verdict: which profile the repository belongs to and what is wrong.</summary>
public static class RepoAnalyzer
{
    /// <param name="guardEnabled">Whether to point out repositories the commit guard cannot check.</param>
    public static RepoReport Analyze(RepoFacts facts, IReadOnlyList<Profile> profiles, bool guardEnabled)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(profiles);
        if (facts.Error is not null)
        {
            return new RepoReport(facts, null, null, [new RepoIssue(RepoIssueCode.GitError, RepoIssueSeverity.Problem, facts.Error)]);
        }

        // Only GitHub remotes matter; TFS, GitLab and local paths are none of GitPersona's business.
        var remotes = facts.Remotes
            .Where(r => r.Repository is { } repo && IsGitHubHost(repo.Host, profiles))
            .OrderBy(r => r.Name == "origin" ? 0 : 1)
            .ToList();

        var byAlias = remotes.Select(r => AliasOwner(profiles, r.Repository!.Host)).FirstOrDefault(p => p is not null);
        var byFolder = ProfileResolver.ForFolder(profiles, facts.Path);
        var byUrl = byAlias is null && byFolder is null && remotes.Count > 0 ? ProfileResolver.ForRepository(profiles, remotes[0].Repository!) : null;

        var (profile, source) = (byAlias, byFolder, byUrl) switch
        {
            ({ } p, _, _) => (p, RepoProfileSource.Alias),
            (null, { } p, _) => (p, RepoProfileSource.Folder),
            (null, null, { Reason: ProfileMatchReason.Username } m) => (m.Profile, RepoProfileSource.Username),
            (null, null, { } m) => (m.Profile, RepoProfileSource.Owner),
            _ => ((Profile?)null, (RepoProfileSource?)null),
        };

        var issues = new List<RepoIssue>();
        if (profile is null)
        {
            if (remotes.Count > 0)
            {
                issues.Add(new RepoIssue(RepoIssueCode.NoProfile, RepoIssueSeverity.Note, remotes[0].Url));
            }

            return new RepoReport(facts, null, null, issues);
        }

        if (remotes.Count > 0)
        {
            AddRemoteIssue(issues, remotes[0], profile, byAlias, byFolder);
        }

        AddEmailIssue(issues, facts, profile);

        if (guardEnabled && !string.IsNullOrWhiteSpace(facts.LocalHooksPath))
        {
            issues.Add(new RepoIssue(RepoIssueCode.OwnHooksPath, RepoIssueSeverity.Note, facts.LocalHooksPath));
        }

        return new RepoReport(facts, profile, source, issues);
    }

    /// <summary>
    /// Checks the remote that pushes (origin, else the first GitHub remote). Others, such as an https "upstream"
    /// of a fork, are only fetched from and are left alone.
    /// </summary>
    private static void AddRemoteIssue(List<RepoIssue> issues, RemoteInfo remote, Profile profile, Profile? byAlias, Profile? byFolder)
    {
        var repository = remote.Repository!;
        if (byAlias is not null && byFolder is not null && byAlias.Id != byFolder.Id
            && repository.Host.Equals(byAlias.HostAlias, StringComparison.OrdinalIgnoreCase))
        {
            var folderUrl = CloneService.RemoteUrlFor(byFolder, repository);
            issues.Add(new RepoIssue(RepoIssueCode.OtherProfilesAlias, RepoIssueSeverity.Problem, remote.Url, folderUrl,
                new RepoFix(RepoFixKind.SetRemoteUrl, remote.Name, folderUrl)));
            return;
        }

        if (repository.Host.Equals(profile.HostAlias, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var aliasUrl = CloneService.RemoteUrlFor(profile, repository);
        var code = remote.IsHttps ? RepoIssueCode.HttpsRemote : RepoIssueCode.SshWithoutAlias;
        issues.Add(new RepoIssue(code, RepoIssueSeverity.Problem, remote.Url, aliasUrl, new RepoFix(RepoFixKind.SetRemoteUrl, remote.Name, aliasUrl)));
    }

    private static void AddEmailIssue(List<RepoIssue> issues, RepoFacts facts, Profile profile)
    {
        var expected = profile.GitUserEmail;
        if (string.IsNullOrWhiteSpace(facts.EffectiveEmail))
        {
            issues.Add(new RepoIssue(RepoIssueCode.EmailMissing, RepoIssueSeverity.Problem, null, expected));
            return;
        }

        if (facts.EffectiveEmail.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // A repository-level user.email overrides every profile rule; removing it is the fix.
        // Otherwise the profile's rules simply do not apply yet (configuration not applied, or remote not on the alias).
        var overridden = !string.IsNullOrWhiteSpace(facts.LocalEmail) && !facts.LocalEmail.Equals(expected, StringComparison.OrdinalIgnoreCase);
        issues.Add(new RepoIssue(RepoIssueCode.EmailMismatch, RepoIssueSeverity.Problem, facts.EffectiveEmail, expected,
            overridden ? new RepoFix(RepoFixKind.UnsetLocalEmail) : null));
    }

    private static Profile? AliasOwner(IReadOnlyList<Profile> profiles, string host) =>
        profiles.FirstOrDefault(p => p.HostAlias.Equals(host, StringComparison.OrdinalIgnoreCase));

    private static bool IsGitHubHost(string host, IReadOnlyList<Profile> profiles) =>
        host.Equals(GitUrlParser.DefaultHost, StringComparison.OrdinalIgnoreCase)
        || profiles.Any(p => p.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || p.HostAlias.Equals(host, StringComparison.OrdinalIgnoreCase));
}
