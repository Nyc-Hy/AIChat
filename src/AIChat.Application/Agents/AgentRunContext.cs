namespace AIChat.Application.Agents;

using AIChat.Abstractions.Configuration;
using AIChat.Domain.Projects;

public sealed class AgentRunContext
{
    public required string ProjectPath { get; init; }
    public IReadOnlyList<string> EnabledToolIds { get; init; } = [];
    public IReadOnlyDictionary<string, ToolPermissionMode> ToolPermissionModes { get; init; } =
        new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase);
    public Func<ToolApprovalRequest, CancellationToken, Task<ToolApprovalDecision>>? RequestToolApprovalAsync { get; init; }
    public int MaxToolRounds { get; init; } = 4;
    public bool AutoVerifyAgentRuns { get; init; }
    public int MaxAutoFixRounds { get; init; } = 3;
    public IReadOnlyList<ProjectVerificationCommand> VerificationCommands { get; init; } = [];
}
