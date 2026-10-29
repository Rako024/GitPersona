using System.Windows;
using GitPersona.Core.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace GitPersona.App.Platform;

public sealed class ThemeService
{
    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;

    private Window? _watchedWindow;

    public void Apply(ThemePreference preference, Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (preference == ThemePreference.System)
        {
            ApplicationThemeManager.ApplySystemTheme(updateAccent: true);
            if (_watchedWindow != window)
            {
                SystemThemeWatcher.Watch(window, Backdrop, updateAccents: true);
                _watchedWindow = window;
            }

            return;
        }

        if (_watchedWindow is not null)
        {
            SystemThemeWatcher.UnWatch(_watchedWindow);
            _watchedWindow = null;
        }

        var theme = preference == ThemePreference.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme, Backdrop, updateAccent: true);
    }
}
