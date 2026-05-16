namespace AIChat.Application.Agents;

public sealed record AgentRunDiagnosticSummary(
    string BlockingReason,
    string NextAction,
    string AttentionSummary);
