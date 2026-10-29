using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitPersona.App.Localization;
using GitPersona.Core.ConfigFiles;
using Microsoft.Extensions.Logging;

namespace GitPersona.App.ViewModels;

public sealed record DiffLineViewModel(DiffLineKind Kind, string Text)
{
    /// <summary>What screen readers and UI Automation announce for the line.</summary>
    public override string ToString() => Text;
}

public sealed class FileDiffViewModel
{
    public FileDiffViewModel(ConfigChange change)
    {
        Path = change.FilePath;
        Title = IssueText.Role(change);
        KindLabel = IssueText.Kind(change.Kind);
        var diff = LineDiff.Compute(change.Original?.Text, change.Updated?.Text);
        Added = diff.Count(l => l.Kind == DiffLineKind.Added);
        Removed = diff.Count(l => l.Kind == DiffLineKind.Removed);
        Lines = diff.Select(line => new DiffLineViewModel(line.Kind, line.Kind switch
        {
            DiffLineKind.Added => "+ " + line.Text,
            DiffLineKind.Removed => "- " + line.Text,
            DiffLineKind.Skipped => LocalizationManager.Instance.Format("Review_Skipped", line.SkippedCount),
            _ => "  " + line.Text,
        })).ToList();
    }

    public string Path { get; }

    public string Title { get; }

    public string KindLabel { get; }

    public int Added { get; }

    public int Removed { get; }

    public string Stats => $"+{Added}  −{Removed}";

    public IReadOnlyList<DiffLineViewModel> Lines { get; }
}

public sealed record PlanIssueViewModel(string Text, bool IsBlocking);

/// <summary>Shows a <see cref="ConfigPlan"/> as diffs and applies it only when the user confirms.</summary>
public sealed partial class DiffPreviewViewModel : LocalizedObject
{
    private readonly ConfigPlan _plan;
    private readonly ConfigWriter _writer;
    private readonly ILogger _logger;

    public DiffPreviewViewModel(ConfigPlan plan, ConfigWriter writer, ILogger logger)
    {
        _plan = plan;
        _writer = writer;
        _logger = logger;
        Files = plan.Changes.Pending.Select(change => new FileDiffViewModel(change)).ToList();
        Issues = plan.Issues.Select(issue => new PlanIssueViewModel(IssueText.For(issue), issue.IsBlocking))
            .OrderByDescending(issue => issue.IsBlocking)
            .ToList();
    }

    public event EventHandler<bool>? CloseRequested;

    public IReadOnlyList<FileDiffViewModel> Files { get; }

    public IReadOnlyList<PlanIssueViewModel> Issues { get; }

    public string? Footer => _plan.IsBlocked ? Loc["Review_Blocked"] : _plan.HasChanges ? null : Loc["Review_NoChanges"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Files written by <see cref="ApplyCommand"/>, once it succeeded.</summary>
    public IReadOnlyList<AppliedChange>? Applied { get; private set; }

    private bool CanApply => _plan.HasChanges && !_plan.IsBlocked && ErrorMessage is null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        try
        {
            Applied = _writer.Apply(_plan.Changes);
            CloseRequested?.Invoke(this, true);
        }
        catch (ConfigConflictException ex)
        {
            ErrorMessage = Loc.Format("Review_Conflict", ex.FilePath);
        }
        catch (ConfigApplyException ex)
        {
            _logger.LogError(ex, "Applying configuration failed");
            ErrorMessage = Loc.Format("Review_ApplyFailed", ex.FilePath, ex.InnerException?.Message);
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
