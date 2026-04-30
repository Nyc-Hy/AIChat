namespace AIChat.Domain.Chat;

// One assistant turn may involve several model/tool steps. AgentRun keeps that
// sequence explicit so the UI can show more than a pile of tool cards.
public sealed class AgentRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; set; } = "";
    public string UserMessageId { get; set; } = "";
    public string AssistantMessageId { get; set; } = "";
    public string Goal { get; set; } = "";
    public AgentRunStatus Status { get; set; } = AgentRunStatus.Running;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<AgentStep> Steps { get; set; } = [];
}
