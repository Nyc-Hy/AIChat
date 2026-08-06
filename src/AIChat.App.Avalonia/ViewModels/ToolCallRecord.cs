using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// 2026-08-05: one row in the AI bubble's
// "工具调用 (N)" expandable section. Replaces
// the previous "one system bubble per tool call"
// pattern that flooded the activity feed on
// long agent runs (a 10-tool run was emitting
// 10–30+ centered "正在读取"/"工具问题" rows
// that pushed the real conversation off-screen).
//
// Lifecycle: a record starts as "运行中" when
// the AgentRunEventType.ToolCall lands, and
// updates to "完成" / "失败" when the matching
// ToolResult arrives. The agent runner keys
// records by tool-call-id (or by name when the
// id is missing, e.g. streaming chunks that
// arrived before the id) so a streaming call
// can be matched to its eventual result row.
public sealed partial class ToolCallRecord : ObservableObject
{
    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    private string summary = "";

    [ObservableProperty]
    private string status = "运行中";

    [ObservableProperty]
    private DateTimeOffset startedAt = DateTimeOffset.Now;

    [ObservableProperty]
    private DateTimeOffset? completedAt;

    public TimeSpan? Duration =>
        CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;

    public string DurationDisplay => Duration switch
    {
        null => "…",
        var d when d.Value.TotalSeconds < 1 => "<1s",
        var d when d.Value.TotalSeconds < 60 => $"{(int)d.Value.TotalSeconds}s",
        var d => $"{(int)d.Value.TotalMinutes}m {d.Value.Seconds}s"
    };

    // Set true when the matching ToolResult lands.
    // Drives the row's status color in the XAML
    // (red for failures, green for success).
    [ObservableProperty]
    private bool isError;

    // Optional: the error message from a failed
    // tool. Empty on success. The XAML shows it
    // inline under the row (one line, truncated)
    // so the user doesn't have to expand the
    // full result to see why a tool failed.
    [ObservableProperty]
    private string errorMessage = "";

    partial void OnCompletedAtChanged(DateTimeOffset? value)
    {
        // Duration + DurationDisplay both
        // derive from CompletedAt — re-raise so
        // the row's timer updates when the
        // tool finishes.
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(DurationDisplay));
    }
}
