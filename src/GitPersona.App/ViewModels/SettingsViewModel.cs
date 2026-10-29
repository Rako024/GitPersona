using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.App.Platform;
using GitPersona.App.Services;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Models;
using Microsoft.Extensions.Logging;

namespace GitPersona.App.ViewModels;

public sealed partial class SettingsViewModel : LocalizedObject
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly ThemeService _theme;
    private readonly AppPaths _paths;
    private readonly ILogger<SettingsViewModel> _logger;

    // Suppresses the side effects of the On...Changed handlers while values are loaded or re-synced.
    private bool _syncing;

    public SettingsViewModel(
        SettingsService settings,
        StartupService startup,
        ThemeService theme,
        AppPaths paths,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _startup = startup;
        _theme = theme;
        _paths = paths;
        _logger = logger;

        _syncing = true;
        Theme = settings.Current.Theme;
        Language = settings.Current.Language;
        StartWithWindows = startup.IsEnabled;
        _syncing = false;
    }

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
