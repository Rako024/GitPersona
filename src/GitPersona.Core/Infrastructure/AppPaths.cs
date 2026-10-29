namespace GitPersona.Core.Infrastructure;

/// <summary>Locations of GitPersona's own data (profiles, settings, logs, hooks).</summary>
public sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary><c>%APPDATA%\GitPersona</c> of the current user.</summary>
    public static AppPaths ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitPersona"));

    public string RootDirectory { get; }

    public string ProfilesFile => Path.Combine(RootDirectory, "profiles.json");

    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public string HooksDirectory => Path.Combine(RootDirectory, "hooks");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
