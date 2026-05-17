using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.Benchmark;

public static class AgentBenchmarkMatcher
{
    public static AgentBenchmarkTask Match(AgentRun run, IReadOnlyList<AgentBenchmarkTask>? tasks = null)
    {
        var candidates = tasks is { Count: > 0 } ? tasks : AgentBenchmarkSuite.DefaultTasks;
        var category = InferCategory(run);
        return candidates.FirstOrDefault(task => string.Equals(task.Category, category, StringComparison.OrdinalIgnoreCase))
               ?? candidates.First();
    }

    public static string InferCategory(AgentRun run)
    {
        var text = string.Join(" ", [
            run.TaskComplexity,
            run.Goal,
            run.ExecutionPolicySummary,
            string.Join(" ", run.EnabledTools)
        ]).ToLowerInvariant();

        if (text.Contains("plugin", StringComparison.Ordinal) ||
            text.Contains("插件", StringComparison.Ordinal) ||
            text.Contains("mcp", StringComparison.Ordinal))
        {
            return "plugins";
        }

        if (text.Contains("test", StringComparison.Ordinal) ||
            text.Contains("测试", StringComparison.Ordinal) ||
            text.Contains("验证", StringComparison.Ordinal) ||
            run.Verifications.Any(verification => !verification.IsSuccess))
        {
            return "verification";
        }

        if (text.Contains("doc", StringComparison.Ordinal) ||
            text.Contains("文档", StringComparison.Ordinal) ||
            text.Contains("readme", StringComparison.Ordinal))
        {
            return "docs";
        }

        if (run.RequiresProjectMutation ||
            run.MutationToolSucceeded ||
            run.FileChanges.Count > 0 ||
            text.Contains("fix", StringComparison.Ordinal) ||
            text.Contains("bug", StringComparison.Ordinal) ||
            text.Contains("修复", StringComparison.Ordinal))
        {
            return "bugfix";
        }

        return "analysis";
    }
}
