using System.Globalization;
using System.IO;
using System.Net.Http;
using GitPersona.App.Platform;
using GitPersona.App.Services;
using GitPersona.App.Tray;
using GitPersona.App.ViewModels;
using GitPersona.App.Views;
using GitPersona.Core.ConfigFiles;
using GitPersona.Core.Git;
using GitPersona.Core.GitHub;
using GitPersona.Core.Hooks;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Profiles;
using GitPersona.Core.Security;
using GitPersona.Core.Settings;
using GitPersona.Core.Ssh;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace GitPersona.App;

internal static class AppHost
{
    public static IHost Build(AppPaths paths)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.AddSerilog(log => log
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "gitpersona-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

        var services = builder.Services;

        // Core
        services.AddSingleton(paths);
        services.AddSingleton(UserPaths.ForCurrentUser());
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ToolLocator>();
        services.AddSingleton(sp => new GitService(sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<ToolLocator>()));
        services.AddSingleton<CloneService>();
        services.AddSingleton<HookService>();
        services.AddSingleton<SshKeyService>();
        services.AddSingleton(_ => new BackupManager());
        services.AddSingleton<ConfigWriter>();
        services.AddSingleton<ConfigPlanner>();
        services.AddSingleton<ProfileStore>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<GitHubTokenStore>();
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton(sp => new GitHubApiService(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ILogger<GitHubApiService>>()));

        // App
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ProfilesService>();
        services.AddSingleton<EditorServices>();
        services.AddSingleton<ConfigReviewService>();
        services.AddSingleton<CommitGuardService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<StartupService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<AppShell>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<CloneViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
