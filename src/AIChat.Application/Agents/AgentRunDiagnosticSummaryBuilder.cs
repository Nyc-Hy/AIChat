using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public static class AgentRunDiagnosticSummaryBuilder
{
    public static AgentRunDiagnosticSummary Build(AgentRun run)
    {
        var blockingReason = BuildBlockingReason(run);
        var nextAction = BuildNextAction(run);
        var attentionSummary = BuildAttentionSummary(run);
        return new AgentRunDiagnosticSummary(blockingReason, nextAction, attentionSummary);
    }

    private static string BuildBlockingReason(AgentRun run)
    {
        return run.Status switch
        {
            AgentRunStatus.Running => string.IsNullOrWhiteSpace(run.CurrentPhaseSummary)
                ? "正在运行。"
                : run.CurrentPhaseSummary,
            AgentRunStatus.BudgetExceeded => "工具调用预算已耗尽，运行已暂停。",
            AgentRunStatus.Cancelled => string.IsNullOrWhiteSpace(run.CompletionReason)
                ? "运行已停止。"
                : run.CompletionReason,
            AgentRunStatus.Failed when run.Verifications.Any(item => !item.IsSuccess) =>
                "验证失败：" + FormatFirstFailedVerification(run),
            AgentRunStatus.Failed when run.ToolApprovalRejectedCount > 0 =>
                $"工具审批被拒绝 {run.ToolApprovalRejectedCount} 次。",
            AgentRunStatus.Failed => string.IsNullOrWhiteSpace(run.CompletionReason)
                ? "运行失败，未记录具体原因。"
                : run.CompletionReason,
            _ => "没有阻塞。"
        };
    }

    private static string BuildNextAction(AgentRun run)
    {
        if (run.Status == AgentRunStatus.Completed)
        {
            return run.Verifications.Count == 0 && run.MutationToolSucceeded
                ? "建议补跑项目验证命令。"
                : "可以验收本轮结果。";
        }

        if (run.Status == AgentRunStatus.Running)
        {
            return "等待当前步骤完成。";
        }

        if (run.ToolBudgetExceeded)
        {
            return "从恢复提示继续，并先确认当前工作区状态。";
        }

        if (run.Verifications.Any(item => !item.IsSuccess))
        {
            return "优先修复失败验证中暴露的最小问题，然后重跑失败命令。";
        }

        if (run.ToolApprovalRejectedCount > 0)
        {
            return "重新评估被拒绝的工具调用，必要时改用只读检查或请求用户授权。";
        }

        if (run.Status == AgentRunStatus.Cancelled)
        {
            return "从最后一个已完成步骤继续，避免重复已经完成的工作。";
        }

        return string.IsNullOrWhiteSpace(run.RecoverySuggestion)
            ? "查看运行步骤和恢复包后继续。"
            : "按恢复提示继续。";
    }

    private static string BuildAttentionSummary(AgentRun run)
    {
        var items = new List<string>();
        if (run.ToolApprovalRejectedCount > 0)
        {
            items.Add($"审批拒绝 {run.ToolApprovalRejectedCount}");
        }

        var failedVerifications = run.Verifications.Count(item => !item.IsSuccess);
        if (failedVerifications > 0)
        {
            items.Add($"验证失败 {failedVerifications}");
        }

        var failedSteps = run.Steps.Count(item => item.Status == AgentStepStatus.Failed);
        if (failedSteps > 0)
        {
            items.Add($"步骤失败 {failedSteps}");
        }

        if (run.ToolBudgetExceeded)
        {
            items.Add("预算耗尽");
        }

        return items.Count == 0 ? "暂无需特别处理的风险。" : string.Join(" · ", items);
    }

    private static string FormatFirstFailedVerification(AgentRun run)
    {
        var failed = run.Verifications.First(item => !item.IsSuccess);
        var command = string.IsNullOrWhiteSpace(failed.Command) ? failed.ToolName : failed.Command;
        return $"{command} 退出码 {failed.ExitCode}";
    }
}
