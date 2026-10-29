using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.App.Services;
using GitPersona.Core.Diagnostics;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Models;
using GitPersona.Core.Security;
using GitPersona.Core.Ssh;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;

namespace GitPersona.App.ViewModels;

public sealed partial class ProfileTestViewModel : LocalizedObject
{
    private readonly SshConnectionTester _tester;
    private readonly ILogger _logger;
    private SshTestResult? _result;

    public ProfileTestViewModel(Profile profile, SshConnectionTester tester, ILogger logger)
    {
        Profile = profile;
        _tester = tester;
        _logger = logger;
    }

    public Profile Profile { get; }

    public string Name => Profile.DisplayName;

    public string Alias => $"git@{Profile.HostAlias}";

    public string Color => Profile.Color;

    public SymbolRegular Icon => Enum.TryParse<SymbolRegular>(Profile.Icon, out var icon) ? icon : SymbolRegular.Person24;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    public partial bool IsTesting { get; set; }

    [ObservableProperty]
    public partial string? Output { get; set; }

    [ObservableProperty]
    public partial DiagnosticSeverity? Severity { get; set; }

    public override string ToString() => Name;

    public string StatusText => IsTesting ? Loc["Diag_Testing"]
        : _result is null ? (Output ?? Loc["Diag_NotTested"])
        : Loc.Format($"SshTest_{_result.Outcome}", _result.Login, Profile.GitHubUsername, Profile.HostAlias);

    private bool CanTest => !IsTesting;

    [RelayCommand(CanExecute = nameof(CanTest))]
    public async Task TestAsync()
    {
        IsTesting = true;
        OnPropertyChanged(nameof(StatusText));
        try
        {
            _result = await _tester.TestAsync(Profile);
            Output = _result.Output;
            Severity = _result.Outcome == SshTestOutcome.Authenticated ? DiagnosticSeverity.Ok : DiagnosticSeverity.Error;
        }
        catch (Exception ex) when (ex is ProcessRunException or ToolNotFoundException)
        {
            _logger.LogWarning(ex, "ssh test of {Profile} failed", Profile.Id);
            _result = null;
            Output = ex.Message;
            Severity = DiagnosticSeverity.Error;
        }
        finally
        {
            IsTesting = false;
            OnPropertyChanged(nameof(StatusText));
        }
    }
}

public sealed class DiagnosticItemViewModel(DiagnosticItem item, Func<DiagnosticItem, Task> act) : LocalizedObject
{
    public DiagnosticItem Item { get; } = item;

    public DiagnosticSeverity Severity => Item.Severity;

    public string Title => Loc.Format($"Diag_{Item.Code}_Title", Item.Subject, Item.Detail ?? Loc["Diag_Unknown"]);

    public string Text => Loc.Format($"Diag_{Item.Code}_Text", Item.Subject, Item.Detail ?? Loc["Diag_Unknown"],
        Item.Variant is { } variant ? Loc[$"SshSource_{variant}"] : null);

    public string? ActionLabel => Item.Action is { } action ? Loc[$"DiagAction_{action.Kind}"] : null;

    public bool IsDestructive => Item.Action?.Kind == DiagnosticActionKind.DeleteCredential;

    public IAsyncRelayCommand ActionCommand { get; } = new AsyncRelayCommand(() => act(item));

    public override string ToString() => Title;
}

/// <summary>SSH test per profile and the Windows-specific checks, each with a confirmed fix where one exists.</summary>
public sealed partial class DiagnosticsViewModel : LocalizedObject, IPageActivation
{
    private readonly ProfilesService _profiles;
    private readonly DiagnosticsService _diagnostics;
    private readonly SshConnectionTester _tester;
    private readonly KnownHostsService _knownHosts;
    private readonly CredentialStore _credentials;
    private readonly SettingsService _settings;
    private readonly ConfigReviewService _review;
    private readonly DialogService _dialogs;
    private readonly UserPaths _paths;
    private readonly ILogger<DiagnosticsViewModel> _logger;

    public DiagnosticsViewModel(
        ProfilesService profiles,
        DiagnosticsService diagnostics,
        SshConnectionTester tester,
        KnownHostsService knownHosts,
        CredentialStore credentials,
        SettingsService settings,
        ConfigReviewService review,
        DialogService dialogs,
        UserPaths paths,
        ILogger<DiagnosticsViewModel> logger)
    {
        _profiles = profiles;
        _diagnostics = diagnostics;
        _tester = tester;
        _knownHosts = knownHosts;
        _credentials = credentials;
        _settings = settings;
        _review = review;
        _dialogs = dialogs;
        _paths = paths;
        _logger = logger;
        profiles.Changed += (_, _) => RefreshProfiles();
        RefreshProfiles();
    }

    public ObservableCollection<ProfileTestViewModel> ProfileTests { get; } = [];

    public ObservableCollection<DiagnosticItemViewModel> Checks { get; } = [];

    public bool HasProfiles => ProfileTests.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunChecksCommand))]
    public partial bool IsChecking { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public void OnNavigatedTo()
    {
        if (!IsChecking)
        {
            _ = RunChecksAsync();
        }
    }

    [RelayCommand]
    private async Task TestAllAsync() => await Task.WhenAll(ProfileTests.Select(t => t.TestAsync()));

    private bool CanRunChecks => !IsChecking;

    [RelayCommand(CanExecute = nameof(CanRunChecks))]
    private async Task RunChecksAsync()
    {
        IsChecking = true;
        try
        {
            var items = await _diagnostics.RunAsync(_profiles.Profiles);
            Checks.Clear();
            foreach (var item in items.OrderByDescending(i => i.Severity))
            {
                Checks.Add(new DiagnosticItemViewModel(item, ActAsync));
            }
        }
        catch (Exception ex) when (ex is ProcessRunException or ToolNotFoundException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Diagnostics failed");
            StatusMessage = Loc.Format("Diag_ActionFailed", ex.Message);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task ActAsync(DiagnosticItem item)
    {
        if (item.Action is not { } action)
        {
            return;
        }

        StatusMessage = null;
        try
        {
            switch (action.Kind)
            {
                case DiagnosticActionKind.CopyCommand:
                    Clipboard.SetText(action.Argument ?? string.Empty);
                    StatusMessage = Loc["Diag_Copied"];
                    return;

                case DiagnosticActionKind.InstallGitHubHostKeys:
                    if (!await ConfirmAsync(item, Loc.Format("DiagConfirm_InstallGitHubHostKeys", _paths.KnownHostsFile), action))
                    {
                        return;
                    }

                    await _knownHosts.InstallGitHubKeysAsync(await _knownHosts.FetchGitHubKeysAsync());
                    break;

                case DiagnosticActionKind.RestrictKeyFile:
                    if (!await ConfirmAsync(item, Loc.Format("DiagConfirm_RestrictKeyFile", action.Argument), action))
                    {
                        return;
                    }

                    KeyFilePermissions.Restrict(action.Argument!);
                    break;

                case DiagnosticActionKind.DeleteCredential:
                    if (!await ConfirmAsync(item, Loc.Format("DiagConfirm_DeleteCredential", action.Argument), action, ControlAppearance.Danger))
                    {
                        return;
                    }

                    _credentials.Delete(action.Argument!);
                    break;

                case DiagnosticActionKind.UseWindowsOpenSsh:
                    _settings.Update(s => s with { UseWindowsOpenSsh = true });
                    if (_review.Review(DialogService.ActiveWindow) is null)
                    {
                        return;
                    }

                    break;
            }

            StatusMessage = Loc["Diag_Done"];
        }
        catch (Exception ex) when (ex is HostKeyVerificationException or HttpRequestException or IOException or UnauthorizedAccessException
            or Win32Exception or COMException or ProcessRunException or ToolNotFoundException or InvalidOperationException)
        {
            _logger.LogError(ex, "Diagnostic action {Action} failed", action.Kind);
            StatusMessage = Loc.Format("Diag_ActionFailed", ex.Message);
        }

        await RunChecksAsync();
    }

    private async Task<bool> ConfirmAsync(DiagnosticItem item, string message, DiagnosticAction action, ControlAppearance appearance = ControlAppearance.Primary) =>
        await _dialogs.AskAsync(Loc.Format($"Diag_{item.Code}_Title", item.Subject, item.Detail), message, Loc[$"DiagAction_{action.Kind}"], primaryAppearance: appearance) == DialogChoice.Primary;

    private void RefreshProfiles()
    {
        ProfileTests.Clear();
        foreach (var profile in _profiles.Profiles)
        {
            ProfileTests.Add(new ProfileTestViewModel(profile, _tester, _logger));
        }

        OnPropertyChanged(nameof(HasProfiles));
    }
}
