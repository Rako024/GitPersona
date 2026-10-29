using System.ComponentModel;
using GitPersona.App.Localization;
using GitPersona.App.Services;
using GitPersona.App.Tray;
using GitPersona.App.ViewModels;
using Wpf.Ui.Controls;

namespace GitPersona.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly AppShell _shell;
    private readonly TrayService _tray;
    private readonly SettingsService _settings;

    public MainWindow(MainWindowViewModel viewModel, AppShell shell, TrayService tray, SettingsService settings)
    {
        _shell = shell;
        _tray = tray;
        _settings = settings;
        DataContext = viewModel;
        InitializeComponent();
    }

    /// <summary>Closing hides the window; GitPersona keeps running in the tray until Exit is chosen.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_shell.IsExiting)
        {
            e.Cancel = true;
            Hide();
            if (!_settings.Current.TrayHintShown)
            {
                var loc = LocalizationManager.Instance;
                _tray.ShowNotification(loc["Tray_StillRunning_Title"], loc["Tray_StillRunning_Message"]);
                _settings.Update(s => s with { TrayHintShown = true });
            }
        }

        base.OnClosing(e);
    }
}
