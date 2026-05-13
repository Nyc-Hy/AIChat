namespace AIChat.Application.Agents;

public sealed class AgentTaskClassifier
{
    public AgentTaskComplexity Classify(string goal, AgentRunContext context)
    {
        var normalized = (goal ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return AgentTaskComplexity.Standard;
        }

        if (LooksComplex(normalized))
        {
            return AgentTaskComplexity.Complex;
        }

        if (LooksLikeSimpleReadOnlyRequest(normalized))
        {
            return AgentTaskComplexity.Simple;
        }

        return AgentTaskComplexity.Standard;
    }

    private static bool LooksComplex(string goal)
    {
        var hints = new[]
        {
            "架构", "重构", "完整实现", "端到端", "多模块", "全局", "大型", "复杂", "设计方案",
            "architecture", "refactor", "end-to-end", "multi-module", "large", "complex", "migration"
        };
        return hints.Any(hint => goal.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeSimpleReadOnlyRequest(string goal)
    {
        var readOnlyHints = new[]
        {
            "解释", "说明", "怎么看", "是什么", "为什么", "总结", "分析一下",
            "explain", "describe", "what is", "why", "summarize"
        };
        return readOnlyHints.Any(hint => goal.Contains(hint, StringComparison.OrdinalIgnoreCase)) &&
               !AgentTaskIntent.HasExplicitWriteIntent(goal);
    }
}
