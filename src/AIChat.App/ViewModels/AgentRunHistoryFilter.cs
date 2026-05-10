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

    public static List<AgentRunHistoryItemViewModel> GatherFromConversation(ConversationViewModel conversation)
    {
        var runsById = conversation.Conversation.AgentRuns.ToDictionary(run => run.Id, StringComparer.Ordinal);
        return conversation.Conversation.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.AgentRunId) && runsById.ContainsKey(message.AgentRunId))
            .Select(message => new AgentRunHistoryItemViewModel
            {
                Conversation = conversation,
                Run = new AgentRunViewModel(runsById[message.AgentRunId])
            })
            .OrderByDescending(item => item.Run.Run.StartedAt)
            .ToList();
    }

    public static IEnumerable<AgentRunHistoryItemViewModel> Apply(
        IEnumerable<AgentRunHistoryItemViewModel> items,
        string filterId)
    {
        return filterId switch
        {
            "retryable" => items.Where(item => item.CanRetry),
            "failed" => items.Where(item => item.Run.Status is AgentRunStatus.Failed or AgentRunStatus.Cancelled or AgentRunStatus.BudgetExceeded),
            "completed" => items.Where(item => item.Run.Status is AgentRunStatus.Completed),
            "running" => items.Where(item => item.Run.Status is AgentRunStatus.Running),
            _ => items
        };
    }
}
