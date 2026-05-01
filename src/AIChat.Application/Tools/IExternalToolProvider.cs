namespace AIChat.Application.Tools;

// Extension point for external tool sources (MCP servers, A2A agents, etc.).
// Implementations register with AgentToolRegistry; the harness discovers
// and invokes tools through the same IAgentTool contract used by built-ins.
public interface IExternalToolProvider
{
    string Id { get; }
    string Name { get; }
    Task<IReadOnlyList<IAgentTool>> GetToolsAsync(CancellationToken cancellationToken = default);
}
