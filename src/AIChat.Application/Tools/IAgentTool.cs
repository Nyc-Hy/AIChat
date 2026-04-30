using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

// Runtime tool implementation. The model sees Definition; AgentRunner calls
// ExecuteAsync when the model selects this tool.
public interface IAgentTool
{
    string Id { get; }
    AgentToolRisk Risk { get; }
    ChatToolDefinition Definition { get; }
    Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default);
    Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default);
}
