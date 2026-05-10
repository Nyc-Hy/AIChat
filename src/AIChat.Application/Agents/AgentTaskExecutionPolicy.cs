using AIChat.Application.Context;

namespace AIChat.Application.Agents;

public sealed record AgentTaskExecutionPolicy(
    AgentTaskComplexity Complexity,
    int MaxToolRounds,
    int SubAgentMaxToolCalls,
    bool UsePlanner,
    bool AllowExplorer);

public sealed class AgentTaskExecutionPolicyBuilder
{
    public AgentTaskExecutionPolicy Build(
        AgentTaskComplexity complexity,
        AgentRunContext context,
        TaskContextPack? contextPack,
        bool isContinuation)
    {
        var baseLimit = Math.Max(1, context.MaxToolRounds);
        var maxToolRounds = complexity switch
        {
            AgentTaskComplexity.Simple => Math.Min(baseLimit, 12),
            AgentTaskComplexity.Standard => Math.Min(baseLimit, 35),
            AgentTaskComplexity.Complex => baseLimit,
            _ => baseLimit
        };

        if (isContinuation)
        {
            maxToolRounds = Math.Min(baseLimit, Math.Max(15, maxToolRounds));
        }

        var hasContext = (contextPack?.IncludedFiles.Count ?? 0) > 0 ||
                         (contextPack?.IncludedSnippets.Count ?? 0) > 0 ||
                         (contextPack?.ArtifactRefs.Count ?? 0) > 0;
        var allowExplorer = complexity == AgentTaskComplexity.Complex ||
                            (complexity == AgentTaskComplexity.Standard && !hasContext);

        return new AgentTaskExecutionPolicy(
            complexity,
            maxToolRounds,
            complexity == AgentTaskComplexity.Complex ? 6 : 3,
            complexity != AgentTaskComplexity.Simple && !isContinuation,
            allowExplorer);
    }
}
