namespace GitPersona.Core.Infrastructure;

/// <summary>Locations of the user's git and ssh configuration that GitPersona manages.</summary>
/// <remarks>
/// Windows OpenSSH always resolves <c>~</c> to <c>%USERPROFILE%</c>, so that is the home we manage.
/// Diagnostics warn when Git for Windows sees a different <c>HOME</c>.
/// </remarks>
public sealed class UserPaths
{
    public UserPaths(string homeDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        HomeDirectory = Path.GetFullPath(homeDirectory);
    }

    public static UserPaths ForCurrentUser() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public string HomeDirectory { get; }

    public string SshDirectory => Path.Combine(HomeDirectory, ".ssh");

    public string SshConfigFile => Path.Combine(SshDirectory, "config");

    public string KnownHostsFile => Path.Combine(SshDirectory, "known_hosts");

    public string GlobalGitConfigFile => Path.Combine(HomeDirectory, ".gitconfig");

    public string ProfileGitConfigFile(string profileId) =>
        Path.Combine(HomeDirectory, $".gitconfig-{profileId}");

    public string DefaultSshKeyFile(string profileId) =>
        Path.Combine(SshDirectory, $"id_ed25519_{profileId}");
}
