namespace AIChat.Application.Agents;

using AIChat.Abstractions.Configuration;

public sealed class AgentRunContext
{
    public required string ProjectPath { get; init; }
    public IReadOnlyList<string> EnabledToolIds { get; init; } = [];
    public IReadOnlyDictionary<string, ToolPermissionMode> ToolPermissionModes { get; init; } =
        new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase);
    public Func<ToolApprovalRequest, CancellationToken, Task<ToolApprovalDecision>>? RequestToolApprovalAsync { get; init; }
    public int MaxToolRounds { get; init; } = 4;
}
