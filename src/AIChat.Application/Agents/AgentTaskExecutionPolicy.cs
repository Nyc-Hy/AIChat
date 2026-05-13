using AIChat.Application.Context;

namespace AIChat.Application.Agents;

public sealed record AgentTaskExecutionPolicy(
    AgentTaskComplexity Complexity,
    string Mode,
    int MaxToolRounds,
    int SubAgentMaxToolCalls,
    bool UsePlanner,
    bool AllowExplorer,
    string StrategyAdjustment = "");

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
            AgentTaskComplexity.Simple => Math.Min(baseLimit, 4),
            AgentTaskComplexity.Standard => Math.Min(baseLimit, 24),
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
        var mode = complexity switch
        {
            AgentTaskComplexity.Simple when !isContinuation => "Fast Path",
            AgentTaskComplexity.Complex => "Full Agent Loop",
            _ => isContinuation ? "Continuation" : "Standard Agent Loop"
        };
        var allowExplorer = complexity == AgentTaskComplexity.Complex ||
                            (complexity == AgentTaskComplexity.Standard && !hasContext);

        return new AgentTaskExecutionPolicy(
            complexity,
            mode,
            maxToolRounds,
            complexity == AgentTaskComplexity.Simple ? 0 : complexity == AgentTaskComplexity.Complex ? 6 : 3,
            complexity != AgentTaskComplexity.Simple && !isContinuation,
            allowExplorer);
    }
}
