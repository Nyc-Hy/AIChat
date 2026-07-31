using AIChat.Domain.Chat;
using CommunityToolkit.Mvvm.ComponentModel;

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

    // Coarse status bucket the XAML uses as a class selector so the
    // status pill / duration text can be coloured by outcome. Keeps
    // the colour map in App.axaml (theming concern) rather than
    // embedding brushes in the view-model.
    public string StatusKind => Status.ToLowerInvariant() switch
    {
        "running" => "running",
        "completed" => "completed",
        "failed" => "failed",
        "skipped" => "skipped",
        "budgetexceeded" => "budget",
        "cancelled" => "cancelled",
        _ => "other"
    };

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
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(IsBudgetExceeded));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(StatusKind));
    }

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
