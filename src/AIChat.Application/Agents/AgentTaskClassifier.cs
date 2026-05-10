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

        if (RequiresProjectMutation(normalized))
        {
            return AgentTaskComplexity.Standard;
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
               !RequiresProjectMutation(goal);
    }

    private static bool RequiresProjectMutation(string goal)
    {
        var mutationWords = new[]
        {
            "创建", "新建", "生成", "实现", "写一个", "做一个", "加一个", "新增",
            "修改", "改成", "改为", "替换", "删除", "修复", "优化", "重构",
            "create", "implement", "write", "modify", "change", "replace", "fix", "update", "add"
        };
        return mutationWords.Any(word => goal.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
