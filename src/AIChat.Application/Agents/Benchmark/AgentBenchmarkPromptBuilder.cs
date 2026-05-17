namespace AIChat.Application.Agents.Benchmark;

public static class AgentBenchmarkPromptBuilder
{
    public static string Build(AgentBenchmarkTask task, string projectName)
    {
        var mutationRequirement = task.RequiresMutation
            ? "允许做满足任务所需的最小代码或文档修改，并记录变更文件。"
            : "只读分析优先，除非发现必须修改的问题，否则不要写文件。";
        var verificationRequirement = task.RequiresVerification
            ? "完成修改后必须运行项目验证命令或最小相关测试。"
            : "如有明确验证命令，可运行最小验证；没有必要时说明未运行验证的原因。";

        return $"""
               运行 AIChat 内置 Benchmark 任务。

               Benchmark：{task.Name}
               分类：{task.Category}
               项目：{projectName}

               任务目标：
               {task.Goal}

               执行要求：
               1. 先快速确认当前项目状态和相关文件。
               2. {mutationRequirement}
               3. {verificationRequirement}
               4. 控制工具调用数量，目标不超过 {task.MaxToolCalls} 次。
               5. 控制上下文体积，目标 prompt 预算不超过约 {task.MaxEstimatedPromptTokens} tokens。
               6. 最终回复必须包含：完成情况、实际改动或分析结论、验证情况、剩余风险。

               这次运行会被用于评估任务完成率、项目分析准确性和 token/工具消耗，请优先选择短路径完成。
               """;
    }
}
