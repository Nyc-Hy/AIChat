using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.Planning;

public sealed record AgentPlanningRequest(
    string Goal,
    string ProjectPath,
    IReadOnlyList<string> EnabledToolIds,
    IReadOnlyList<ChatMessage> Messages);
