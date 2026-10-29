using GitPersona.Core.ConfigFiles;
using GitPersona.Core.Profiles;

namespace GitPersona.App.Localization;

/// <summary>User-facing text for validation and planning issues reported by Core.</summary>
public static class IssueText
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    public static string For(ProfileIssue issue) => Loc.Format($"Issue_Profile_{issue.Code}", issue.Value);

    public static string For(PlanIssue issue) => Loc.Format($"Issue_Plan_{issue.Code}", issue.ProfileId, issue.Value);

    public static string Role(ConfigChange change) => change.Role switch
    {
        ConfigFileRole.ProfileGitConfig => Loc.Format("Role_ProfileGitConfig", change.ProfileId),
        _ => Loc[$"Role_{change.Role}"],
    };

    public static string Kind(ConfigChangeKind kind) => Loc[$"Kind_{kind}"];
}
