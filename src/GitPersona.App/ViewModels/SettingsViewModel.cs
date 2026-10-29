using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.App.Platform;
using GitPersona.App.Services;
using GitPersona.Core.GitHub;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Models;
using GitPersona.Core.Security;
using Microsoft.Extensions.Logging;

namespace GitPersona.App.ViewModels;

public sealed partial class SettingsViewModel : LocalizedObject
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly ThemeService _theme;
    private readonly AppPaths _paths;
    private readonly CommitGuardService _guard;
    private readonly ConfigReviewService _review;
    private readonly DialogService _dialogs;
    private readonly ICredentialStore _credentials;
    private readonly ILogger<SettingsViewModel> _logger;

    // Suppresses the side effects of the On...Changed handlers while values are loaded or re-synced.
    private bool _syncing;

    public SettingsViewModel(
        SettingsService settings,
        StartupService startup,
        ThemeService theme,
        AppPaths paths,
        CommitGuardService guard,
        ConfigReviewService review,
        DialogService dialogs,
        ICredentialStore credentials,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _startup = startup;
        _theme = theme;
        _paths = paths;
        _guard = guard;
        _review = review;
        _dialogs = dialogs;
        _credentials = credentials;
        _logger = logger;

        _syncing = true;
        Theme = settings.Current.Theme;
        Language = settings.Current.Language;
        StartWithWindows = startup.IsEnabled;
        GitHubOAuthClientId = settings.Current.GitHubOAuthClientId ?? string.Empty;
        CommitGuardEnabled = settings.Current.CommitGuardEnabled;
        UseWindowsOpenSsh = settings.Current.UseWindowsOpenSsh;
        _syncing = false;
        settings.Changed += (_, current) =>
        {
            // Diagnostics and "Remove configuration" change these too; keep the toggles in step.
            _syncing = true;
            UseWindowsOpenSsh = current.UseWindowsOpenSsh;
            CommitGuardEnabled = current.CommitGuardEnabled;
            _syncing = false;
        };
    }

    [ObservableProperty]
    public partial string? RemovalMessage { get; set; }

    /// <summary>Everything GitPersona added to git/ssh goes, after review; profiles and keys stay.</summary>
    [RelayCommand]
    private async Task RemoveConfigurationAsync()
    {
        RemovalMessage = null;
        var choice = await _dialogs.AskAsync(Loc["Settings_Remove"], Loc["Settings_Remove_Confirm"], Loc["Settings_Remove_Button"],
            primaryAppearance: Wpf.Ui.Controls.ControlAppearance.Danger);
        if (choice != DialogChoice.Primary)
        {
            return;
        }

        var previous = _settings.Current;
        _settings.Update(s => s with { CommitGuardEnabled = false, UseWindowsOpenSsh = false });
        IReadOnlyList<Core.ConfigFiles.AppliedChange>? applied = null;
        try
        {
            applied = _review.ReviewRemoval(Application.Current.MainWindow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Planning the removal failed");
        }

        if (applied is null)
        {
            _settings.Update(_ => previous);
            return;
        }

        await _guard.SyncAsync();
        try
        {
            foreach (var token in _credentials.Enumerate(GitHubTokenStore.Prefix + "*"))
            {
                _credentials.Delete(token.TargetName);
            }
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove saved GitHub sign-ins");
        }

        RemovalMessage = Loc["Settings_Removed"];
    }

    [ObservableProperty]
    public partial bool UseWindowsOpenSsh { get; set; }

    partial void OnUseWindowsOpenSshChanged(bool value)
    {
        if (!_syncing)
        {
            _ = ApplyWindowsOpenSshAsync(value);
        }
    }

    private async Task ApplyWindowsOpenSshAsync(bool enabled)
    {
        await Task.Yield();
        _settings.Update(s => s with { UseWindowsOpenSsh = enabled });
        try
        {
            _review.Review(Application.Current.MainWindow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Planning the configuration failed");
        }
    }

    [ObservableProperty]
    public partial bool CommitGuardEnabled { get; set; }

    partial void OnCommitGuardEnabledChanged(bool value)
    {
        if (!_syncing)
        {
            _ = ApplyCommitGuardAsync(value);
        }
    }

    /// <summary>Hooks first, then the reviewed core.hooksPath change, so git never points at a missing folder.</summary>
    private async Task ApplyCommitGuardAsync(bool enabled)
    {
        // Let the toggle's binding update finish before a modal dialog opens.
        await Task.Yield();
        _settings.Update(s => s with { CommitGuardEnabled = enabled });
        await _guard.SyncAsync();
        try
        {
            _review.Review(Application.Current.MainWindow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Planning the configuration failed");
        }
    }

    [ObservableProperty]
    public partial string GitHubOAuthClientId { get; set; }

    partial void OnGitHubOAuthClientIdChanged(string value)
    {
        if (!_syncing)
        {
            _settings.Update(s => s with { GitHubOAuthClientId = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
        }
    }

    [RelayCommand]
    private void RegisterOAuthApp() =>
        Process.Start(new ProcessStartInfo(GitHubEndpoints.For(Profile.DefaultHost).NewOAuthAppPage.AbsoluteUri) { UseShellExecute = true })?.Dispose();

    public IReadOnlyList<LocalizedOption<ThemePreference>> ThemeOptions { get; } =
    [
        new(ThemePreference.System, "Theme_System"),
        new(ThemePreference.Light, "Theme_Light"),
        new(ThemePreference.Dark, "Theme_Dark"),
    ];

    public IReadOnlyList<LocalizedOption<UiLanguage>> LanguageOptions { get; } =
    [
        new(UiLanguage.System, "Language_System"),
        new(UiLanguage.Azerbaijani, "Language_Azerbaijani"),
        new(UiLanguage.English, "Language_English"),
    ];

    [ObservableProperty]
    public partial ThemePreference Theme { get; set; }

    [ObservableProperty]
    public partial UiLanguage Language { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    public string DataFolderNote => Loc.Format("Settings_Data_Description", _paths.RootDirectory);

    public string VersionText => Loc.Format("Settings_Version", AppVersion);

    public static string AppVersion
    {
        get
        {
            var version = typeof(SettingsViewModel).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var metadata = version.IndexOf('+', StringComparison.Ordinal);
            return metadata < 0 ? version : version[..metadata];
        }
    }

    partial void OnThemeChanged(ThemePreference value)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(s => s with { Theme = value });
        if (Application.Current.MainWindow is { } window)
        {
            _theme.Apply(value, window);
        }
    }

    partial void OnLanguageChanged(UiLanguage value)
    {
        if (_syncing)
        {
            return;
        }

        _settings.Update(s => s with { Language = value });
        Loc.SetLanguage(value);
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }

        try
        {
            if (value)
            {
                _startup.Enable();
            }
            else
            {
                _startup.Disable();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            _logger.LogError(ex, "Could not change the startup registration");
            System.Windows.MessageBox.Show(Loc.Format("Error_StartupToggle", ex.Message), "GitPersona");
        }

        // Show what Windows will actually do, whatever happened above.
        _syncing = true;
        StartWithWindows = _startup.IsEnabled;
        _syncing = false;
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(_paths.RootDirectory);

    [RelayCommand]
    private void OpenLogsFolder() => OpenFolder(_paths.LogsDirectory);

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
    }
}
