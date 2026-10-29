using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.App.Services;
using GitPersona.Core.Models;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;

namespace GitPersona.App.ViewModels;

public sealed class ProfileListItem(Profile profile) : LocalizedObject
{
    public Profile Profile { get; } = profile;

    public string Id => Profile.Id;

    public string DisplayName => Profile.DisplayName;

    public string Email => Profile.GitUserEmail;

    public string Alias => Profile.HostAlias;

    public string Color => Profile.Color;

    public SymbolRegular Icon => Enum.TryParse<SymbolRegular>(Profile.Icon, out var icon) ? icon : SymbolRegular.Person24;

    public string? KeyWarning =>
        !string.IsNullOrWhiteSpace(Profile.SshKeyPath) && File.Exists(Profile.SshKeyPath) ? null : Loc["Profiles_NoKey"];

    public override string ToString() => DisplayName;
}

public sealed partial class ProfilesViewModel : LocalizedObject
{
    private readonly EditorServices _services;
    private readonly ProfilesService _profiles;
    private readonly ConfigReviewService _review;
    private readonly DialogService _dialogs;
    private readonly ILogger<ProfilesViewModel> _logger;
    private bool _suppressSelection;

    public ProfilesViewModel(EditorServices services, ConfigReviewService review, ILogger<ProfilesViewModel> logger)
    {
        _services = services;
        _profiles = services.Profiles;
        _review = review;
        _dialogs = services.Dialogs;
        _logger = logger;
        _profiles.Changed += (_, _) => RefreshList();
        RefreshList();
        RefreshPlanSummary();
    }

    public ObservableCollection<ProfileListItem> Items { get; } = [];

    [ObservableProperty]
    public partial ProfileListItem? SelectedItem { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState), nameof(ShowSelectHint))]
    public partial ProfileEditorViewModel? Editor { get; set; }

    [ObservableProperty]
    public partial string? PlanSummary { get; set; }

    [ObservableProperty]
    public partial bool PlanNeedsAttention { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public bool ShowEmptyState => Items.Count == 0 && Editor is null;

    public bool ShowSelectHint => Items.Count > 0 && Editor is null;

    /// <summary>Opens a profile, e.g. from the tray menu.</summary>
    public void Select(string id) => SelectedItem = Items.FirstOrDefault(item => item.Id == id);

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        RefreshPlanSummary();
    }

    partial void OnSelectedItemChanged(ProfileListItem? oldValue, ProfileListItem? newValue)
    {
        if (!_suppressSelection)
        {
            _ = SwitchToAsync(oldValue, newValue?.Id);
        }
    }

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        if (await ConfirmLeaveEditorAsync())
        {
            SelectSilently(null);
            OpenEditor(null);
        }
    }

    [RelayCommand]
    private void ApplyConfiguration() => Review(onlyWhenNeeded: false);

    private async Task SwitchToAsync(ProfileListItem? previous, string? nextId)
    {
        if (!await ConfirmLeaveEditorAsync())
        {
            SelectSilently(previous?.Id);
            return;
        }

        SelectSilently(nextId);
        OpenEditor(_profiles.Profiles.FirstOrDefault(p => p.Id == nextId));
    }

    /// <returns><see langword="false"/> when the user wants to keep editing.</returns>
    private async Task<bool> ConfirmLeaveEditorAsync()
    {
        if (Editor is not { IsDirty: true } editor)
        {
            return true;
        }

        var choice = await _dialogs.AskAsync(
            Loc["Editor_UnsavedTitle"],
            Loc.Format("Editor_UnsavedPrompt", editor.Heading),
            Loc["Common_Save"],
            Loc["Common_Discard"]);
        return choice switch
        {
            DialogChoice.Primary => editor.TrySave(),
            DialogChoice.Secondary => true,
            _ => false,
        };
    }

    private void OpenEditor(Profile? profile)
    {
        if (Editor is { } old)
        {
            old.Saved -= OnEditorSaved;
            old.Deleted -= OnEditorDeleted;
        }

        Editor = new ProfileEditorViewModel(profile, _services, _logger);
        Editor.Saved += OnEditorSaved;
        Editor.Deleted += OnEditorDeleted;
        StatusMessage = null;
    }

    private void OnEditorSaved(object? sender, string id)
    {
        SelectSilently(id);
        Review(onlyWhenNeeded: true);
    }

    private void OnEditorDeleted(object? sender, string id)
    {
        SelectSilently(null);
        Editor = null;
        Review(onlyWhenNeeded: true);
    }

    /// <param name="onlyWhenNeeded">After a save: skip the dialog when nothing on disk would change.</param>
    private void Review(bool onlyWhenNeeded)
    {
        try
        {
            if (onlyWhenNeeded && _review.Plan() is { HasChanges: false, IsBlocked: false })
            {
                return;
            }

            if (_review.Review(DialogService.ActiveWindow) is { } applied)
            {
                StatusMessage = Loc.Format("Profiles_Applied", applied.Count);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Planning the configuration failed");
            StatusMessage = ex.Message;
        }
        finally
        {
            RefreshPlanSummary();
        }
    }

    private void RefreshList()
    {
        var selectedId = SelectedItem?.Id;
        _suppressSelection = true;
        Items.Clear();
        foreach (var profile in _profiles.Profiles)
        {
            Items.Add(new ProfileListItem(profile));
        }

        SelectedItem = Items.FirstOrDefault(item => item.Id == selectedId);
        _suppressSelection = false;
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowSelectHint));
    }

    private void RefreshPlanSummary()
    {
        try
        {
            var plan = _review.Plan();
            (PlanSummary, PlanNeedsAttention) = plan switch
            {
                { IsBlocked: true } => (Loc["Profiles_PlanBlocked"], true),
                { HasChanges: true } => (Loc.Format("Profiles_PendingChanges", plan.Changes.Pending.Count), true),
                _ => (Items.Count > 0 ? Loc["Profiles_UpToDate"] : null, false),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not compute the configuration plan");
            (PlanSummary, PlanNeedsAttention) = (ex.Message, true);
        }
    }

    private void SelectSilently(string? id)
    {
        _suppressSelection = true;
        SelectedItem = id is null ? null : Items.FirstOrDefault(item => item.Id == id);
        _suppressSelection = false;
    }
}
