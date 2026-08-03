using AIChat.Domain.Chat;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Lightweight view-model for a single sub-agent run. Wraps the
// persisted AgentSubAgentRun with display strings (template name,
// duration, status) and lets the plan panel show the live progress
// of every sub-agent the harness has dispatched.
//
// The harness emits two events per sub-agent — SubAgentStarted and
// SubAgentCompleted — and each carries the full AgentSubAgentRun.
// Upsert into the host's collection by Id so a re-emitted or
// late-arriving completion updates the same row the user already
// sees in the panel.
public sealed partial class SubAgentRunViewModel : ObservableObject
{
    public string Id { get; }
    public string TemplateDisplay { get; }
    public string Task { get; }
    public DateTimeOffset StartedAt { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsSkipped))]
    [NotifyPropertyChangedFor(nameof(IsBudgetExceeded))]
    [NotifyPropertyChangedFor(nameof(IsCancelled))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(CanStop))]
    private string status;

    [ObservableProperty]
    private int toolCallCount;

    [ObservableProperty]
    private string durationDisplay;

    [ObservableProperty]
    private string? summary;

    public bool IsRunning => Status.Equals("Running", StringComparison.OrdinalIgnoreCase);
    public bool IsCompleted => Status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
    public bool IsFailed => Status.Equals("Failed", StringComparison.OrdinalIgnoreCase);
    public bool IsSkipped => Status.Equals("Skipped", StringComparison.OrdinalIgnoreCase);
    public bool IsBudgetExceeded => Status.Equals("BudgetExceeded", StringComparison.OrdinalIgnoreCase);
    public bool IsCancelled => Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

    // Per-row status color, matching the Codex-style "colored dot
    // per state" visual. We use a small fixed palette of
    // green / red / amber / grey so the panel stays readable
    // without the per-run list having to know about theme tokens.
    // Re-raised on Status change via [NotifyPropertyChangedFor].
    public IBrush StatusBrush => IsCompleted
        ? new SolidColorBrush(Color.Parse("#5cd6a8"))
        : IsFailed
            ? new SolidColorBrush(Color.Parse("#ff6b6b"))
            : IsRunning
                ? new SolidColorBrush(Color.Parse("#f5a623"))
                : new SolidColorBrush(Color.Parse("#9aa0a6"));

    public SubAgentRunViewModel(AgentSubAgentRun run)
    {
        Id = run.Id;
        TemplateDisplay = FormatTemplateName(run.TemplateId);
        Task = run.Task;
        Status = string.IsNullOrWhiteSpace(run.Status) ? "Running" : run.Status;
        ToolCallCount = run.ToolCallCount;
        Summary = string.IsNullOrWhiteSpace(run.Summary) ? null : run.Summary;
        StartedAt = run.StartedAt;
        DurationDisplay = FormatDuration(run.StartedAt, run.CompletedAt);
    }

    public void Update(AgentSubAgentRun run)
    {
        Status = string.IsNullOrWhiteSpace(run.Status) ? "Running" : run.Status;
        ToolCallCount = run.ToolCallCount;
        Summary = string.IsNullOrWhiteSpace(run.Summary) ? null : run.Summary;
        DurationDisplay = FormatDuration(StartedAt, run.CompletedAt);
        // The Is* booleans re-raise automatically via
        // [NotifyPropertyChangedFor] on the Status field above, so
        // no manual OnPropertyChanged calls are needed here. The
        // previous explicit re-raises were a workaround for the
        // missing attribute — keep this comment as the breadcrumb
        // if anyone ever needs to revisit the propagation rules.
        // Re-raise CanStop because the host may have wired
        // StopCommand after this row was created, and the
        // IsRunning transition would otherwise leave the
        // button permanently hidden.
        OnPropertyChanged(nameof(CanStop));
    }

    // 2026-08-03: per-row 'stop' button. The XAML binds
    // Command="{Binding StopCommand}" with the button's
    // IsVisible tied to IsRunning so the affordance only
    // appears for in-flight runs. The command delegates to
    // the host (AgentHostViewModel) which holds the
    // SubAgentScheduler instance; the per-row VM does not
    // need a direct reference to the scheduler.
    public IRelayCommand? StopCommand { get; set; }

    public bool CanStop => IsRunning && StopCommand?.CanExecute(Id) == true;

    // Match the naming AgentHarness uses for the explorer template.
    // Other templates are skipped by the current coordinator, but the
    // display is future-proof so a new template just gets a sensible
    // label automatically.
    private static string FormatTemplateName(string templateId) => templateId.ToLowerInvariant() switch
    {
        "explorer" => "Explorer",
        "researcher" => "Researcher",
        "verifier" => "Verifier",
        _ => string.IsNullOrEmpty(templateId) ? "Sub-agent" : templateId
    };

    private static string FormatDuration(DateTimeOffset startedAt, DateTimeOffset? completedAt)
    {
        if (completedAt is null)
        {
            return "运行中…";
        }
        var span = completedAt.Value - startedAt;
        if (span.TotalSeconds < 1)
        {
            return "<1s";
        }
        if (span.TotalSeconds < 60)
        {
            return $"{(int)span.TotalSeconds}s";
        }
        return $"{(int)span.TotalMinutes}m {span.Seconds}s";
    }
}
