using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public sealed class AgentRunQualityEvaluator
{
    public AgentRunQualityEvaluation Evaluate(AgentRun run)
    {
        var score = 100;
        var findings = new List<string>();

        if (run.Status == AgentRunStatus.Completed)
        {
            findings.Add("任务完成");
        }
        else
        {
            score -= run.Status == AgentRunStatus.BudgetExceeded ? 20 : 35;
            findings.Add(run.Status == AgentRunStatus.BudgetExceeded ? "预算耗尽暂停" : $"状态：{run.Status}");
        }

        if (run.ToolBudgetExceeded)
        {
            score -= 15;
            findings.Add("工具预算耗尽");
        }

        var failedVerificationCount = run.Verifications.Count(item => !item.IsSuccess);
        if (failedVerificationCount > 0)
        {
            score -= Math.Min(30, failedVerificationCount * 15);
            findings.Add($"验证失败 {failedVerificationCount} 个");
        }
        else if (run.Verifications.Count > 0)
        {
            findings.Add($"验证通过 {run.Verifications.Count} 个");
        }

        if (run.ToolApprovalRejectedCount > 0)
        {
            score -= Math.Min(20, run.ToolApprovalRejectedCount * 10);
            findings.Add($"工具被拒绝 {run.ToolApprovalRejectedCount} 次");
        }

        if (HasConsistencyRisk(run))
        {
            score -= 25;
            findings.Add("存在结果一致性风险");
        }

        if (run.ModelCallCount > 3)
        {
            score -= Math.Min(12, (run.ModelCallCount - 3) * 3);
            findings.Add($"模型调用偏多：{run.ModelCallCount} 次");
        }

        if (run.ContextEstimatedTokens > 2000)
        {
            score -= 8;
            findings.Add($"上下文较重：约 {run.ContextEstimatedTokens} tokens");
        }

        score = Math.Clamp(score, 0, 100);
        return new AgentRunQualityEvaluation(
            score,
            string.Join("；", findings),
            CreateStrategySuggestion(run, score));
    }

    private static bool HasConsistencyRisk(AgentRun run)
    {
        return run.FinalValidationSummary.Contains("一致性风险", StringComparison.OrdinalIgnoreCase) ||
               run.FinalValidationSummary.Contains("存在风险", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateStrategySuggestion(AgentRun run, int score)
    {
        var mode = ExtractMode(run.ExecutionPolicySummary);
        if (run.ToolBudgetExceeded)
        {
            return "同类任务建议提高工具预算，或使用继续任务从 checkpoint 接着执行。";
        }

        if (run.Verifications.Any(item => !item.IsSuccess))
        {
            return "写入任务建议保持自动验证开启，并优先修复失败验证。";
        }

        if (run.ToolApprovalRejectedCount > 0)
        {
            return "同类任务建议先说明需要的高风险工具，再等待用户授权。";
        }

        if (HasConsistencyRisk(run))
        {
            return "建议要求 Agent 基于工具记录汇报结果，避免无证据声明。";
        }

        if (string.Equals(mode, "Fast Path", StringComparison.OrdinalIgnoreCase) &&
            score >= 90 &&
            run.ModelCallCount <= 1 &&
            run.ToolCallCount <= Math.Max(1, run.MaxToolRounds))
        {
            return "Fast Path 表现稳定，同类简单任务可继续使用轻量策略。";
        }

        if (run.ExplorerUsed && run.SubAgentRuns.Count == 0)
        {
            return "Explorer 已启用但未产生子 Agent 结果，可降低同类任务的 explorer 触发。";
        }

        return score >= 85
            ? "策略表现良好，保持当前执行模式。"
            : "建议复查本次运行指标，降低上下文或调整预算。";
    }

    private static string ExtractMode(string summary)
    {
        const string prefix = "mode=";
        var start = summary.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return "";
        }

        start += prefix.Length;
        var end = summary.IndexOf(';', start);
        return (end < 0 ? summary[start..] : summary[start..end]).Trim();
    }
}

public sealed record AgentRunQualityEvaluation(
    int Score,
    string Summary,
    string StrategySuggestion);
