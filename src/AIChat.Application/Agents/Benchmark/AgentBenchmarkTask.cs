namespace AIChat.Application.Agents.Benchmark;

public sealed record AgentBenchmarkTask(
    string Id,
    string Name,
    string Category,
    string Goal,
    bool RequiresMutation = false,
    bool RequiresVerification = false,
    int MaxToolCalls = 12,
    int MaxEstimatedPromptTokens = 8000);
