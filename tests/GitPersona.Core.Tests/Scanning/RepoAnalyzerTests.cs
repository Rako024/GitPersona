using GitPersona.Core.Models;
using GitPersona.Core.Scanning;

namespace GitPersona.Core.Tests.Scanning;

public class RepoAnalyzerTests
{
    private static readonly Profile Work = TestProfiles.Create("work", @"C:\h", @"C:\work") with { Owners = ["acme"] };
    private static readonly Profile Personal = TestProfiles.Create("personal", @"C:\h") with { Owners = [], GitHubUsername = "me" };
    private static readonly Profile[] Profiles = [Work, Personal];

    private static RepoFacts Facts(string path, string? email, params (string Name, string Url)[] remotes) =>
        new(path, remotes.Select(r => new RemoteInfo(r.Name, r.Url)).ToList(), email, LocalEmail: null, LocalHooksPath: null);

    private static RepoReport Analyze(RepoFacts facts, bool guard = false) => RepoAnalyzer.Analyze(facts, Profiles, guard);

    [Fact]
    public void A_repository_on_its_profiles_alias_with_the_right_email_is_clean()
    {
        var report = Analyze(Facts(@"C:\work\api", "work@example.com", ("origin", "git@github-work:acme/api.git")));

        Assert.Equal(Work, report.Profile);
        Assert.Equal(RepoProfileSource.Alias, report.Source);
        Assert.Empty(report.Issues);
    }

    [Theory]
    [InlineData("https://github.com/acme/api.git", RepoIssueCode.HttpsRemote)]
    [InlineData("git@github.com:acme/api.git", RepoIssueCode.SshWithoutAlias)]
    public void Remotes_not_on_the_alias_get_a_set_url_fix(string url, RepoIssueCode code)
    {
        var report = Analyze(Facts(@"C:\work\api", "work@example.com", ("origin", url)));

        var issue = Assert.Single(report.Issues);
        Assert.Equal(code, issue.Code);
        Assert.Equal(RepoIssueSeverity.Problem, issue.Severity);
        Assert.Equal(new RepoFix(RepoFixKind.SetRemoteUrl, "origin", "git@github-work:acme/api.git"), issue.Fix);
        Assert.Equal(RepoProfileSource.Folder, report.Source);
    }

    [Fact]
    public void Outside_profile_folders_the_owner_rule_decides()
    {
        var report = Analyze(Facts(@"D:\misc\api", "work@example.com", ("origin", "https://github.com/ACME/api")));

        Assert.Equal(Work, report.Profile);
        Assert.Equal(RepoProfileSource.Owner, report.Source);
        Assert.Equal(RepoIssueCode.HttpsRemote, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void Only_the_pushing_remote_is_checked()
    {
        var report = Analyze(Facts(@"C:\work\fork", "work@example.com",
            ("upstream", "https://github.com/original/fork.git"),
            ("origin", "git@github-work:acme/fork.git")));

        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Another_profiles_alias_inside_a_profile_folder_is_flagged()
    {
        var report = Analyze(Facts(@"C:\work\side", "personal@example.com", ("origin", "git@github-personal:me/side.git")));

        Assert.Equal(Personal, report.Profile);
        var issue = Assert.Single(report.Issues);
        Assert.Equal(RepoIssueCode.OtherProfilesAlias, issue.Code);
        Assert.Equal("git@github-work:me/side.git", issue.Fix?.NewUrl);
    }

    [Fact]
    public void A_local_email_override_gets_an_unset_fix()
    {
        var facts = Facts(@"C:\work\api", "old@corp.example", ("origin", "git@github-work:acme/api.git")) with { LocalEmail = "old@corp.example" };

        var issue = Assert.Single(Analyze(facts).Issues);

        Assert.Equal(RepoIssueCode.EmailMismatch, issue.Code);
        Assert.Equal("old@corp.example", issue.Current);
        Assert.Equal("work@example.com", issue.Expected);
        Assert.Equal(new RepoFix(RepoFixKind.UnsetLocalEmail), issue.Fix);
    }

    [Fact]
    public void A_wrong_email_without_override_has_no_repository_fix()
    {
        var issue = Assert.Single(Analyze(Facts(@"C:\work\api", "global@corp.example", ("origin", "git@github-work:acme/api.git"))).Issues);

        Assert.Equal(RepoIssueCode.EmailMismatch, issue.Code);
        Assert.Null(issue.Fix);
    }

    [Fact]
    public void A_missing_email_is_a_problem()
    {
        Assert.Equal(RepoIssueCode.EmailMissing, Assert.Single(Analyze(Facts(@"C:\work\api", null)).Issues).Code);
    }

    [Fact]
    public void Non_github_remotes_are_not_managed()
    {
        var report = Analyze(Facts(@"D:\tfs\legacy", "me@corp.example", ("origin", "https://tfs.corp.example/tfs/DefaultCollection/_git/legacy")));

        Assert.Null(report.Profile);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void A_github_repository_no_profile_claims_is_only_noted()
    {
        var report = Analyze(Facts(@"D:\misc\tool", "me@corp.example", ("origin", "https://github.com/stranger/tool.git")));

        Assert.Null(report.Profile);
        Assert.Equal(RepoIssueSeverity.Note, Assert.Single(report.Issues).Severity);
        Assert.False(report.HasProblems);
    }

    [Fact]
    public void Own_hooks_path_is_noted_only_while_the_guard_is_on()
    {
        var facts = Facts(@"C:\work\web", "work@example.com", ("origin", "git@github-work:acme/web.git")) with { LocalHooksPath = ".husky/_" };

        Assert.Empty(Analyze(facts, guard: false).Issues);
        Assert.Equal(RepoIssueCode.OwnHooksPath, Assert.Single(Analyze(facts, guard: true).Issues).Code);
    }

    [Fact]
    public void Git_errors_are_reported()
    {
        var report = Analyze(new RepoFacts(@"C:\work\x", [], null, null, null, "fatal: detected dubious ownership in repository"));

        Assert.Equal(RepoIssueCode.GitError, Assert.Single(report.Issues).Code);
    }
}
