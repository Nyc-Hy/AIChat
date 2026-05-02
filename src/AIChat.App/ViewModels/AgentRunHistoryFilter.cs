using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public static class AgentRunHistoryFilter
{
    public static IReadOnlyList<SelectionOptionViewModel> Options { get; } =
    [
        new() { Id = "all", Name = "全部" },
        new() { Id = "retryable", Name = "可重试" },
        new() { Id = "failed", Name = "失败/停止" },
        new() { Id = "completed", Name = "已完成" },
        new() { Id = "running", Name = "运行中" }
    ];

    public static IEnumerable<AgentRunHistoryItemViewModel> Apply(
        IEnumerable<AgentRunHistoryItemViewModel> items,
        string filterId)
    {
        return filterId switch
        {
            "retryable" => items.Where(item => item.CanRetry),
            "failed" => items.Where(item => item.Run.Status is AgentRunStatus.Failed or AgentRunStatus.Cancelled),
            "completed" => items.Where(item => item.Run.Status is AgentRunStatus.Completed),
            "running" => items.Where(item => item.Run.Status is AgentRunStatus.Running),
            _ => items
        };
    }
}
