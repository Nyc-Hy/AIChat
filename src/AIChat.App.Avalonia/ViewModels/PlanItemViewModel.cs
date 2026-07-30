using AIChat.Domain.Chat;

namespace AIChat.App.Avalonia.ViewModels;

// Wraps a domain AgentPlanItem with a Glyph property so the XAML can
// render the right status icon without a value converter. Kept in the
// UI layer so the domain model stays free of display concerns.
public sealed class PlanItemViewModel
{
    public string Title { get; init; } = "";
    public AgentPlanItemStatus Status { get; init; }

    public string Glyph => Status switch
    {
        AgentPlanItemStatus.Pending => "○",
        AgentPlanItemStatus.InProgress => "◐",
        AgentPlanItemStatus.Completed => "✓",
        AgentPlanItemStatus.Blocked => "⚠",
        AgentPlanItemStatus.Skipped => "⊘",
        _ => "·"
    };

    public bool IsCompleted => Status == AgentPlanItemStatus.Completed;
}
