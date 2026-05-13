using AIChat.Application.Agents;

namespace AIChat.App.ViewModels;

public static class AgentRunHistoryInsights
{
    public static string Build(IReadOnlyList<AgentRunHistoryItemViewModel> items)
    {
        return AgentRunHistoryInsightBuilder.Build(items.Select(item => item.Run.Run).ToList());
    }
}
