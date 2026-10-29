using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.App.Services;
using GitPersona.Core.Git;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Profiles;
using Microsoft.Extensions.Logging;

namespace GitPersona.App.ViewModels;

/// <summary>A page that wants to refresh itself when it becomes visible.</summary>
public interface IPageActivation
{
    void OnNavigatedTo();
}

/// <summary>Paste a GitHub URL; the profile, SSH alias URL and folder are derived from it.</summary>
public sealed partial class CloneViewModel : LocalizedObject, IPageActivation, IDisposable
{
    private const int OutputLines = 8;

    private readonly ProfilesService _profiles;
    private readonly CloneService _clone;
    private readonly GitService _git;
    private readonly ConfigReviewService _review;
    private readonly DialogService _dialogs;
    private readonly ILogger<CloneViewModel> _logger;
    private ProfileMatch? _match;
    private bool _settingTarget;
    private bool _targetTouched;
    private CancellationTokenSource? _cancellation;

    public CloneViewModel(
        ProfilesService profiles,
        CloneService clone,
        GitService git,
        ConfigReviewService review,
        DialogService dialogs,
        ILogger<CloneViewModel> logger)
    {
        _profiles = profiles;
        _clone = clone;
        _git = git;
        _review = review;
        _dialogs = dialogs;
        _logger = logger;
        Url = string.Empty;
        TargetFolder = string.Empty;
        profiles.Changed += (_, _) => RefreshProfiles();
        RefreshProfiles();
    }

    public ObservableCollection<ProfileListItem> Profiles { get; } = [];

    public ObservableCollection<string> Output { get; } = [];

    public bool HasProfiles => Profiles.Count > 0;

    [ObservableProperty]
    public partial string Url { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RememberOwnerText))]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial RepoRef? Repository { get; set; }

    [ObservableProperty]
    public partial string? UrlError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial ProfileListItem? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string? MatchText { get; set; }

    [ObservableProperty]
    public partial bool CanRememberOwner { get; set; }

    [ObservableProperty]
    public partial bool RememberOwner { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    public partial string TargetFolder { get; set; }

    [ObservableProperty]
    public partial string? RemoteUrl { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand), nameof(StopCommand))]
    public partial bool IsCloning { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string? PhaseText { get; set; }

    [ObservableProperty]
    public partial string? ResultText { get; set; }

    [ObservableProperty]
    public partial string? ResultDetail { get; set; }

    [ObservableProperty]
    public partial bool ResultIsError { get; set; }

    [ObservableProperty]
    public partial string? ClonedPath { get; set; }

    [ObservableProperty]
    public partial bool ConfigPending { get; set; }

    public string? RememberOwnerText => Repository is null ? null : Loc.Format("Clone_RememberOwner", Repository.Owner);

    public void Dispose() => _cancellation?.Dispose();

    public void OnNavigatedTo()
    {
        RefreshConfigPending();
        if (string.IsNullOrWhiteSpace(Url) && !IsCloning)
        {
            PrefillFromClipboard();
        }
    }

    /// <summary>Uses the clipboard if it holds a GitHub repository URL (tray: "Clone from URL…").</summary>
    public void PrefillFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() is { } text && GitUrlParser.Parse(text) is not null)
            {
                Url = text.Trim();
            }
        }
        catch (COMException)
        {
            // Another application holds the clipboard; the user can still paste by hand.
        }
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Resolve(selectProfile: false);
    }

    partial void OnUrlChanged(string value)
    {
        Repository = GitUrlParser.Parse(value);
        UrlError = Repository is null && !string.IsNullOrWhiteSpace(value) ? Loc["Clone_NotRecognized"] : null;
        _targetTouched = false;
        ClearResult();
        Resolve(selectProfile: true);
    }

    partial void OnSelectedProfileChanged(ProfileListItem? value) => UpdateDerived();

    partial void OnTargetFolderChanged(string value)
    {
        if (!_settingTarget)
        {
            _targetTouched = true;
        }
    }

    private bool CanClone => Repository is not null && SelectedProfile is not null && !string.IsNullOrWhiteSpace(TargetFolder) && !IsCloning;

    [RelayCommand(CanExecute = nameof(CanClone))]
    private async Task CloneAsync()
    {
        var profile = SelectedProfile!.Profile;
        var repository = Repository!;
        var remote = CloneService.RemoteUrlFor(profile, repository);
        var target = TargetFolder.Trim();
        ClearResult();

        if (RememberOwner && CanRememberOwner)
        {
            SaveOwnerRule(profile, repository.Owner);
        }

        IsCloning = true;
        Progress = 0;
        Output.Clear();
        _cancellation = new CancellationTokenSource();
        try
        {
            await _clone.CloneAsync(
                remote,
                target,
                new Progress<CloneProgress>(p =>
                {
                    Progress = p.Overall;
                    PhaseText = p.Phase == ClonePhase.Starting ? Loc["Clone_Phase_Starting"] : $"{Loc[$"Clone_Phase_{p.Phase}"]} {p.Percent}%";
                }),
                new Progress<string>(AddOutput),
                _cancellation.Token);

            Progress = 100;
            ClonedPath = Path.GetFullPath(target);
            ResultText = Loc.Format("Clone_Succeeded", ClonedPath);
            await DescribeIdentityAsync(ClonedPath, profile.GitUserEmail);
        }
        catch (OperationCanceledException)
        {
            ResultText = Loc["Clone_Cancelled"];
        }
        catch (CloneFailedException ex)
        {
            ResultIsError = true;
            var subject = ex.Failure == CloneFailure.TargetNotEmpty ? target : profile.HostAlias;
            ResultText = Loc.Format($"Clone_Failed_{ex.Failure}", subject, ex.Message);
            ResultDetail = ex.Failure == CloneFailure.Other ? null : ex.Message;
        }
        catch (Exception ex) when (ex is ToolNotFoundException or ProcessRunException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogError(ex, "Clone failed");
            ResultIsError = true;
            ResultText = Loc.Format("Clone_Failed_Other", profile.HostAlias, ex.Message);
        }
        finally
        {
            IsCloning = false;
            PhaseText = null;
            _cancellation.Dispose();
            _cancellation = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsCloning))]
    private void Stop() => _cancellation?.Cancel();

    [RelayCommand]
    private void Paste()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                Url = Clipboard.GetText().Trim();
            }
        }
        catch (COMException)
        {
            // Clipboard busy; nothing to paste.
        }
    }

    [RelayCommand]
    private void BrowseTarget()
    {
        var start = string.IsNullOrWhiteSpace(TargetFolder) ? null : Path.GetDirectoryName(TargetFolder.Trim());
        if (_dialogs.PickFolder(start) is { } folder)
        {
            // The picked folder is the parent; the repository gets its own folder inside it.
            TargetFolder = Repository is null ? folder : Path.Combine(folder, Repository.Name);
        }
    }

    [RelayCommand]
    private void OpenClonedFolder()
    {
        if (ClonedPath is not null && Directory.Exists(ClonedPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { ClonedPath }, UseShellExecute = false })?.Dispose();
        }
    }

    [RelayCommand]
    private void ApplyConfiguration()
    {
        try
        {
            _review.Review(DialogService.ActiveWindow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Planning the configuration failed");
        }

        RefreshConfigPending();
    }

    private void Resolve(bool selectProfile)
    {
        _match = Repository is null ? null : ProfileResolver.ForRepository(_profiles.Profiles, Repository);
        if (selectProfile && Repository is not null)
        {
            // No match: force a conscious choice instead of keeping whatever was selected before.
            SelectedProfile = _match is null ? null : Profiles.FirstOrDefault(p => p.Id == _match.Profile.Id);
        }

        MatchText = (Repository, _match) switch
        {
            (null, _) => null,
            (_, null) => Loc.Format("Clone_Match_None", Repository.Owner),
            (_, { Reason: ProfileMatchReason.Alias }) => Loc.Format("Clone_Match_Alias", Repository.Host),
            (_, { Reason: ProfileMatchReason.Owner }) => Loc.Format("Clone_Match_Owner", Repository.Owner),
            _ => Loc.Format("Clone_Match_Username", Repository.Owner),
        };
        UpdateDerived();
    }

    private void UpdateDerived()
    {
        var profile = SelectedProfile?.Profile;
        RemoteUrl = profile is not null && Repository is not null ? CloneService.RemoteUrlFor(profile, Repository) : null;
        CanRememberOwner = profile is not null && Repository is not null
            && !profile.Owners.Contains(Repository.Owner, StringComparer.OrdinalIgnoreCase)
            && _match?.Reason is not ProfileMatchReason.Alias;
        if (!CanRememberOwner)
        {
            RememberOwner = false;
        }

        if (!_targetTouched)
        {
            _settingTarget = true;
            TargetFolder = profile is not null && Repository is not null ? CloneService.DefaultTargetFor(profile, Repository) ?? string.Empty : string.Empty;
            _settingTarget = false;
        }
    }

    private void SaveOwnerRule(Core.Models.Profile profile, string owner)
    {
        try
        {
            _profiles.Save(profile with { Owners = [.. profile.Owners, owner] }, profile.Id);
        }
        catch (Exception ex) when (ex is ProfileValidationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remember owner {Owner} for {Profile}", owner, profile.Id);
            ResultDetail = Loc.Format("Clone_RememberFailed", ex.Message);
        }
    }

    private async Task DescribeIdentityAsync(string repository, string expected)
    {
        try
        {
            var email = await _git.GetConfigAsync(repository, "user.email");
            (ResultDetail, ResultIsError) = email switch
            {
                null or "" => (Loc["Clone_EmailMissing"], true),
                _ when email.Equals(expected, StringComparison.OrdinalIgnoreCase) => (Loc.Format("Clone_EmailOk", email), false),
                _ => (Loc.Format("Clone_EmailMismatch", email, expected), true),
            };
        }
        catch (Exception ex) when (ex is ProcessRunException or ToolNotFoundException)
        {
            _logger.LogWarning(ex, "Could not read user.email of {Repository}", repository);
        }
    }

    private void RefreshProfiles()
    {
        var selectedId = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var profile in _profiles.Profiles)
        {
            Profiles.Add(new ProfileListItem(profile));
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId);
        OnPropertyChanged(nameof(HasProfiles));
        Resolve(selectProfile: SelectedProfile is null);
    }

    private void RefreshConfigPending()
    {
        try
        {
            ConfigPending = _profiles.Profiles.Count > 0 && _review.Plan().HasChanges;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not compute the configuration plan");
            ConfigPending = false;
        }
    }

    private void AddOutput(string line)
    {
        Output.Add(line);
        while (Output.Count > OutputLines)
        {
            Output.RemoveAt(0);
        }
    }

    private void ClearResult()
    {
        ResultText = null;
        ResultDetail = null;
        ResultIsError = false;
        ClonedPath = null;
    }
}
