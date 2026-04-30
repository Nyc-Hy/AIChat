using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class ToolExecutionRequest
{
    public required ChatToolCall ToolCall { get; init; }
    public required string ProjectPath { get; init; }
    public IReadOnlyDictionary<string, ToolPermissionMode> ToolPermissionModes { get; init; } =
        new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> SessionAllowedToolIds { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public Func<ToolApprovalRequest, CancellationToken, Task<ToolApprovalDecision>>? RequestToolApprovalAsync { get; init; }
}
