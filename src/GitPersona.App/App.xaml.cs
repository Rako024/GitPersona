using System.Windows;
using System.Windows.Threading;
using GitPersona.App.Localization;
using GitPersona.App.Platform;
using GitPersona.App.Services;
using GitPersona.App.Tray;
using GitPersona.App.ViewModels;
using GitPersona.App.Views;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GitPersona.App;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private IHost? _host;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = SingleInstance.TryAcquire();
        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var paths = AppPaths.ForCurrentUser();
        paths.EnsureCreated();
        _host = AppHost.Build(paths);
        _host.Start();
        var services = _host.Services;

        _logger = services.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var settings = services.GetRequiredService<SettingsService>().Current;
        LocalizationManager.Instance.SetLanguage(settings.Language);

        var window = services.GetRequiredService<MainWindow>();
        MainWindow = window;
        services.GetRequiredService<ThemeService>().Apply(settings.Theme, window);
        services.GetRequiredService<TrayService>().Initialize();

        var shell = services.GetRequiredService<AppShell>();
        _singleInstance.ListenForActivation(() => Dispatcher.BeginInvoke(shell.ShowMainWindow));

        var minimized = e.Args.Contains(StartupService.MinimizedArgument, StringComparer.OrdinalIgnoreCase);
        if (!minimized)
        {
            shell.ShowMainWindow();
        }

        _logger.LogInformation("GitPersona {Version} started (minimized: {Minimized})", SettingsViewModel.AppVersion, minimized);
        WarnAboutQuarantinedProfiles(services.GetRequiredService<ProfileStore>());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("GitPersona exiting");
        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose(); // also disposes the tray icon and flushes the log
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static void WarnAboutQuarantinedProfiles(ProfileStore store)
    {
        store.Load();
        if (store.QuarantinedFile is { } path)
        {
            MessageBox.Show(LocalizationManager.Instance.Format("Error_ProfilesQuarantined", path), "GitPersona",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception");
        MessageBox.Show(LocalizationManager.Instance.Format("Error_Unexpected", e.Exception.Message), "GitPersona",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception; terminating: {Terminating}", e.IsTerminating);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
