using GitPersona.Core.GitHub;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Security;
using GitPersona.Core.Ssh;

namespace GitPersona.App.Services;

/// <summary>Everything a profile editor needs, so a new editor can be created per selected profile.</summary>
public sealed record EditorServices(
    ProfilesService Profiles,
    SshKeyService Keys,
    DialogService Dialogs,
    UserPaths Paths,
    GitHubApiService GitHub,
    GitHubTokenStore Tokens,
    SettingsService Settings);
