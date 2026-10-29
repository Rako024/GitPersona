using System.Windows;
using GitPersona.App.ViewModels;
using GitPersona.App.Views;
using GitPersona.Core.ConfigFiles;
using GitPersona.Core.Hooks;
using GitPersona.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace GitPersona.App.Services;

/// <summary>Plans the configuration for the saved profiles and lets the user review it before anything is written.</summary>
public sealed class ConfigReviewService(
    ConfigPlanner planner,
    ConfigWriter writer,
    ProfilesService profiles,
    SettingsService settings,
    HookService hooks,
    ToolLocator tools,
    ILogger<ConfigReviewService> logger)
{
    /// <summary>Raised after the user applied a reviewed plan.</summary>
    public event EventHandler? Applied;

    public ConfigPlan Plan() => planner.Plan(
        profiles.Profiles,
        settings.Current.CommitGuardEnabled ? hooks.HooksDirectory : null,
        settings.Current.UseWindowsOpenSsh ? tools.WindowsOpenSsh("ssh") : null);

    /// <returns>The files written, or <see langword="null"/> when the user cancelled.</returns>
    public IReadOnlyList<AppliedChange>? Review(Window? owner)
    {
        var viewModel = new DiffPreviewViewModel(Plan(), writer, logger);
        var window = new DiffPreviewWindow(viewModel) { Owner = owner };
        if (window.ShowDialog() != true)
        {
            return null;
        }

        Applied?.Invoke(this, EventArgs.Empty);
        return viewModel.Applied;
    }
}
