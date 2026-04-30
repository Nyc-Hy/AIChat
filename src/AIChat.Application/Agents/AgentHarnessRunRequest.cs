using AIChat.Abstractions.Configuration;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public sealed class AgentHarnessRunRequest
{
    public required Conversation Conversation { get; init; }
    public required string UserMessageId { get; init; }
    public required string AssistantMessageId { get; init; }
    public required string Goal { get; init; }
    public required ChatRequest ChatRequest { get; init; }
    public required AppSettings Settings { get; init; }
    public required AgentRunContext Context { get; init; }
}
