namespace GitPersona.Core.Infrastructure;

/// <summary>Locations of GitPersona's own data (profiles, settings, logs, hooks).</summary>
public sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>Overrides the data folder, e.g. to try GitPersona against a sandbox.</summary>
    public const string OverrideVariable = "GITPERSONA_DATA_DIR";

    /// <summary><c>%APPDATA%\GitPersona</c> of the current user, unless <see cref="OverrideVariable"/> is set.</summary>
    public static AppPaths ForCurrentUser() =>
        new(Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitPersona"));

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
