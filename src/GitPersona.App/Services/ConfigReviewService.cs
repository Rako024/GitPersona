using System.Windows;
using GitPersona.App.ViewModels;
using GitPersona.App.Views;
using GitPersona.Core.ConfigFiles;
using Microsoft.Extensions.Logging;

namespace GitPersona.App.Services;

/// <summary>Plans the configuration for the saved profiles and lets the user review it before anything is written.</summary>
public sealed class ConfigReviewService(ConfigPlanner planner, ConfigWriter writer, ProfilesService profiles, ILogger<ConfigReviewService> logger)
{
    public ConfigPlan Plan() => planner.Plan(profiles.Profiles);

    /// <returns>The files written, or <see langword="null"/> when the user cancelled.</returns>
    public IReadOnlyList<AppliedChange>? Review(Window? owner)
    {
        var viewModel = new DiffPreviewViewModel(Plan(), writer, logger);
        var window = new DiffPreviewWindow(viewModel) { Owner = owner };
        return window.ShowDialog() == true ? viewModel.Applied : null;
    }
}
