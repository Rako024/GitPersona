namespace GitPersona.Core.Models;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public enum UiLanguage
{
    System,
    Azerbaijani,
    English,
}

public sealed record AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    public UiLanguage Language { get; init; } = UiLanguage.System;

    /// <summary>Installs the global pre-commit identity check (<c>core.hooksPath</c>).</summary>
    public bool CommitGuardEnabled { get; init; }

    /// <summary>Client ID of the user's own GitHub OAuth App, used for the device flow.</summary>
    public string? GitHubOAuthClientId { get; init; }

    public int ScanMaxDepth { get; init; } = 5;

    /// <summary>Folders scanned in addition to every profile's folders.</summary>
    public IReadOnlyList<string> ExtraScanFolders { get; init; } = [];

    /// <summary>Whether the "still running in the tray" notification was already shown once.</summary>
    public bool TrayHintShown { get; init; }
}
