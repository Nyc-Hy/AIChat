using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public sealed class ToolApprovalRequest
{
    public required ChatToolCall ToolCall { get; init; }
    public required AgentToolPreview Preview { get; init; }
}
