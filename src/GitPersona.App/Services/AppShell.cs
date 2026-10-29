using System.Windows;
using GitPersona.App.ViewModels;
using GitPersona.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GitPersona.App.Services;

/// <summary>Application-level actions shared by the tray, the window and other instances.</summary>
public sealed class AppShell(IServiceProvider services)
{
    /// <summary>Set once the user chose Exit; until then closing the window only hides it.</summary>
    public bool IsExiting { get; private set; }

    public void ShowMainWindow()
    {
        var window = services.GetRequiredService<MainWindow>();
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    public void Navigate(AppPage page)
    {
        services.GetRequiredService<MainWindowViewModel>().NavigateTo(page);
        ShowMainWindow();
    }

    public void ShowProfile(string profileId)
    {
        Navigate(AppPage.Profiles);
        services.GetRequiredService<ProfilesViewModel>().Select(profileId);
    }

    public void Exit()
    {
        IsExiting = true;
        Application.Current.Shutdown();
    }
}
