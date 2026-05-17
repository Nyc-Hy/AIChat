using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.Benchmark;

public sealed record AgentBenchmarkResult(
    string TaskId,
    string Name,
    bool Passed,
    AgentRunOutcomeKind Outcome,
    int QualityScore,
    int ToolCallCount,
    int EstimatedPromptTokens,
    string Summary);
