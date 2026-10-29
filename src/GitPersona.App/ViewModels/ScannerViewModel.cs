using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.App.Services;
using GitPersona.Core.Infrastructure;
using GitPersona.Core.Infrastructure.Processes;
using GitPersona.Core.Scanning;
using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;

namespace GitPersona.App.ViewModels;

/// <param name="IsExtra">Added on the Scanner page (removable), as opposed to a profile folder.</param>
public sealed record ScanRoot(string Path, string Label, bool IsExtra);

public sealed class RepoIssueViewModel(RepoIssue issue, Func<RepoIssue, Task> fix) : LocalizedObject
{
    public RepoIssue Issue { get; } = issue;

    public string Text => IssueText.For(Issue);

    public bool IsProblem => Issue.Severity == RepoIssueSeverity.Problem;

    public bool HasFix => Issue.Fix is not null;

    public IAsyncRelayCommand FixCommand { get; } = new AsyncRelayCommand(() => fix(issue));

    public override string ToString() => Text;
}

public sealed class RepoReportViewModel : LocalizedObject
{
    public RepoReportViewModel(RepoReport report, Func<RepoReportViewModel, RepoIssue, Task> fix)
    {
        Report = report;
        Issues = report.Issues.Select(issue => new RepoIssueViewModel(issue, i => fix(this, i))).ToList();
    }

    public RepoReport Report { get; }

    public string Name => Report.Name;

    public string Path => Report.Facts.Path;

    public string ProfileName => Report.Profile?.DisplayName ?? Loc["Scanner_NoProfile"];

    public string? SourceText => Report.Source is { } source ? Loc[$"Scanner_Source_{source}"] : null;

    public string Color => Report.Profile?.Color ?? "#94A3B8";

    public SymbolRegular Icon => Enum.TryParse<SymbolRegular>(Report.Profile?.Icon, out var icon) ? icon : SymbolRegular.QuestionCircle24;

    public string RemoteUrl => Report.MainRemote?.Url ?? Loc["Scanner_NoRemote"];

    public string EmailText => Loc.Format("Scanner_Email", Report.Facts.EffectiveEmail ?? Loc["Guard_NotSet"]);

    public bool HasProblems => Report.HasProblems;

    public IReadOnlyList<RepoIssueViewModel> Issues { get; }

    public override string ToString() => Name;
}

/// <summary>Lists working copies under the profile folders and extra folders, with problems and one-click fixes.</summary>
public sealed partial class ScannerViewModel : LocalizedObject, IPageActivation, IDisposable
{
    private readonly ProfilesService _profiles;
    private readonly SettingsService _settings;
    private readonly RepoScanner _scanner;
    private readonly RepoFixer _fixer;
    private readonly DialogService _dialogs;
    private readonly ILogger<ScannerViewModel> _logger;
    private CancellationTokenSource? _cancellation;

    public ScannerViewModel(
        ProfilesService profiles,
        SettingsService settings,
        RepoScanner scanner,
        RepoFixer fixer,
        DialogService dialogs,
        ILogger<ScannerViewModel> logger)
    {
        _profiles = profiles;
        _settings = settings;
        _scanner = scanner;
        _fixer = fixer;
        _dialogs = dialogs;
        _logger = logger;
        ReportsView = CollectionViewSource.GetDefaultView(Reports);
        ReportsView.Filter = item => !OnlyProblems || item is RepoReportViewModel { HasProblems: true };
        profiles.Changed += (_, _) => RefreshRoots();
        RefreshRoots();
    }

    public ObservableCollection<ScanRoot> Roots { get; } = [];

    public ObservableCollection<RepoReportViewModel> Reports { get; } = [];

    public ICollectionView ReportsView { get; }

    public bool HasRoots => Roots.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand), nameof(StopCommand), nameof(FixAllCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool OnlyProblems { get; set; }

    [ObservableProperty]
    public partial string? Summary { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public void OnNavigatedTo() => RefreshRoots();

    /// <summary>Tray: "Scan repositories".</summary>
    public void StartScan()
    {
        if (ScanCommand.CanExecute(null))
        {
            ScanCommand.Execute(null);
        }
    }

    public void Dispose() => _cancellation?.Dispose();

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        RefreshRoots();
        UpdateSummary();
    }

    partial void OnOnlyProblemsChanged(bool value) => ReportsView.Refresh();

    private bool CanScan => !IsBusy && HasRoots;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        Reports.Clear();
        _cancellation = new CancellationTokenSource();
        var profiles = _profiles.Profiles;
        var guard = _settings.Current.CommitGuardEnabled;
        try
        {
            await _scanner.ScanAsync(
                Roots.Select(r => r.Path).ToList(),
                _settings.Current.ScanMaxDepth,
                new Progress<RepoFacts>(facts =>
                {
                    Insert(new RepoReportViewModel(RepoAnalyzer.Analyze(facts, profiles, guard), FixAsync));
                    Summary = Loc.Format("Scanner_Scanning", Reports.Count);
                }),
                _cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Keep what was found so far.
        }
        finally
        {
            IsBusy = false;
            _cancellation.Dispose();
            _cancellation = null;
            UpdateSummary();
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Stop() => _cancellation?.Cancel();

    private bool CanFixAll => !IsBusy && Reports.Any(r => r.Issues.Any(i => i.HasFix));

    [RelayCommand(CanExecute = nameof(CanFixAll))]
    private async Task FixAllAsync()
    {
        var targets = Reports.Where(r => ReportsView.Filter(r)).SelectMany(r => r.Issues.Where(i => i.HasFix).Select(i => (Report: r, i.Issue))).ToList();
        var repositories = targets.Select(t => t.Report).Distinct().Count();
        var choice = await _dialogs.AskAsync(Loc["Scanner_FixTitle"], Loc.Format("Scanner_FixAllConfirm", targets.Count, repositories), Loc["Scanner_FixAll"]);
        if (choice != DialogChoice.Primary)
        {
            return;
        }

        IsBusy = true;
        var failures = new List<string>();
        try
        {
            foreach (var group in targets.GroupBy(t => t.Report))
            {
                foreach (var (_, issue) in group)
                {
                    if (await TryApplyAsync(group.Key, issue) is { } error)
                    {
                        failures.Add(error);
                    }
                }

                await RescanAsync(group.Key);
            }
        }
        finally
        {
            IsBusy = false;
        }

        StatusMessage = failures.Count == 0 ? Loc.Format("Scanner_Fixed", targets.Count) : string.Join("\n", failures);
        UpdateSummary();
    }

    [RelayCommand]
    private void AddFolder()
    {
        if (_dialogs.PickFolder() is not { } folder)
        {
            return;
        }

        _settings.Update(s => s with
        {
            ExtraScanFolders = s.ExtraScanFolders.Contains(folder, StringComparer.OrdinalIgnoreCase) ? s.ExtraScanFolders : [.. s.ExtraScanFolders, folder],
        });
        RefreshRoots();
    }

    [RelayCommand]
    private void RemoveFolder(ScanRoot root)
    {
        _settings.Update(s => s with { ExtraScanFolders = s.ExtraScanFolders.Where(f => !f.Equals(root.Path, StringComparison.OrdinalIgnoreCase)).ToList() });
        RefreshRoots();
    }

    [RelayCommand]
    private void OpenRepository(RepoReportViewModel report)
    {
        if (Directory.Exists(report.Path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { report.Path }, UseShellExecute = false })?.Dispose();
        }
    }

    /// <summary>One fix from a repository card, after the user confirmed exactly what changes.</summary>
    private async Task FixAsync(RepoReportViewModel report, RepoIssue issue)
    {
        if (issue.Fix is not { } fix || IsBusy)
        {
            return;
        }

        var message = fix.Kind == RepoFixKind.SetRemoteUrl
            ? Loc.Format("Scanner_FixConfirm_SetRemoteUrl", fix.Remote, report.Name, issue.Current, fix.NewUrl)
            : Loc.Format("Scanner_FixConfirm_UnsetLocalEmail", report.Name, report.Report.Facts.LocalEmail, issue.Expected);
        if (await _dialogs.AskAsync(Loc["Scanner_FixTitle"], message, Loc["Scanner_Fix"]) != DialogChoice.Primary)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = await TryApplyAsync(report, issue) ?? Loc.Format("Scanner_Fixed", 1);
            await RescanAsync(report);
        }
        finally
        {
            IsBusy = false;
            UpdateSummary();
        }
    }

    /// <returns>An error message, or <see langword="null"/> on success.</returns>
    private async Task<string?> TryApplyAsync(RepoReportViewModel report, RepoIssue issue)
    {
        try
        {
            await _fixer.ApplyAsync(report.Path, issue.Fix!);
            return null;
        }
        catch (Exception ex) when (ex is RepoFixException or ProcessRunException or ToolNotFoundException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Fix {Fix} failed in {Repository}", issue.Fix, report.Path);
            return Loc.Format("Scanner_FixFailed", report.Name, ex.Message);
        }
    }

    private async Task RescanAsync(RepoReportViewModel report)
    {
        var facts = await _scanner.ReadFactsAsync(report.Path);
        var index = Reports.IndexOf(report);
        if (index >= 0)
        {
            Reports[index] = new RepoReportViewModel(RepoAnalyzer.Analyze(facts, _profiles.Profiles, _settings.Current.CommitGuardEnabled), FixAsync);
        }
    }

    /// <summary>Problems first, then by name.</summary>
    private void Insert(RepoReportViewModel report)
    {
        var index = 0;
        while (index < Reports.Count && Compare(Reports[index], report) <= 0)
        {
            index++;
        }

        Reports.Insert(index, report);
    }

    private static int Compare(RepoReportViewModel a, RepoReportViewModel b) =>
        a.HasProblems != b.HasProblems ? (a.HasProblems ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    private void UpdateSummary()
    {
        Summary = Reports.Count == 0 && !IsBusy ? null : Loc.Format("Scanner_Summary", Reports.Count, Reports.Count(r => r.HasProblems));
        FixAllCommand.NotifyCanExecuteChanged();
    }

    private void RefreshRoots()
    {
        Roots.Clear();
        foreach (var profile in _profiles.Profiles)
        {
            foreach (var folder in profile.Folders.Where(Path.IsPathFullyQualified))
            {
                Roots.Add(new ScanRoot(folder, Loc.Format("Scanner_RootProfile", profile.DisplayName), IsExtra: false));
            }
        }

        foreach (var folder in _settings.Current.ExtraScanFolders.Where(f => !Roots.Any(r => r.Path.Equals(f, StringComparison.OrdinalIgnoreCase))))
        {
            Roots.Add(new ScanRoot(folder, string.Empty, IsExtra: true));
        }

        OnPropertyChanged(nameof(HasRoots));
        ScanCommand.NotifyCanExecuteChanged();
    }
}
