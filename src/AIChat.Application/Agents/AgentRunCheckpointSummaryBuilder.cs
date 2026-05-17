using AIChat.Application.Verification;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public static class AgentRunCheckpointSummaryBuilder
{
    public static string Build(AgentRun run)
    {
        var lines = new List<string>
        {
            $"目标：{run.Goal}",
            $"当前阶段：{run.Phase}",
            $"工具调用：{run.ToolCallCount}/{(run.MaxToolRounds <= 0 ? "未记录" : run.MaxToolRounds.ToString())}",
            $"文件变更：{run.FileChanges.Count}",
            $"验证：{FormatVerificationCheckpoint(run)}",
            $"工具审批：需要 {run.ToolApprovalRequiredCount} 次，拒绝 {run.ToolApprovalRejectedCount} 次，本会话允许 {run.ToolSessionAllowedCount} 次",
            $"最终状态：{(string.IsNullOrWhiteSpace(run.FinalStatusReason) ? run.Status.ToString() : run.FinalStatusReason)}"
        };

        if (!string.IsNullOrWhiteSpace(run.CompletionReason))
        {
            lines.Add("结束原因：" + run.CompletionReason);
        }

        if (!string.IsNullOrWhiteSpace(run.FinalValidationSummary))
        {
            lines.Add("结束校验：" + Truncate(run.FinalValidationSummary, 300));
        }

        AddPlanLines(run, lines);
        AddChangedFiles(run, lines);
        AddRecentSteps(run, lines);
        AddRecentErrors(run, lines);
        AddArtifactRefs(run, lines);

        var next = GetNextStepSuggestion(run);
        if (!string.IsNullOrWhiteSpace(next))
        {
            lines.Add("下一步建议：" + next);
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildVerificationRecoveryPacket(AgentRun run)
    {
        var failed = run.Verifications
            .Where(verification => !verification.IsSuccess)
            .Take(5)
            .ToList();
        if (failed.Count == 0)
        {
            return "";
        }

        var changedFiles = run.FileChanges
            .Select(change => change.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var lines = new List<string>
        {
            $"失败验证：{failed.Count}/{run.Verifications.Count}",
            "失败命令："
        };

        foreach (var verification in failed)
        {
            lines.Add($"- {verification.Command} (exit {verification.ExitCode}{(verification.TimedOut ? ", timeout" : "")})");
            var summary = string.IsNullOrWhiteSpace(verification.Summary)
                ? VerificationResultParser.Summarize(verification.Output, maxLines: 6)
                : verification.Summary;
            if (!string.IsNullOrWhiteSpace(summary))
            {
                lines.Add("  错误摘要：" + Truncate(summary, 500));
            }
        }

        if (changedFiles.Count > 0)
        {
            lines.Add("优先检查最近修改文件：" + string.Join("；", changedFiles));
        }

        lines.Add("恢复动作：先复现失败命令，只做最小修复，修复后重跑同一失败命令。");
        return string.Join(Environment.NewLine, lines);
    }

    public static string GetNextStepSuggestion(AgentRun run)
    {
        if (run.Verifications.Any(verification => !verification.IsSuccess))
        {
            return "优先修复失败验证，并在修改后重新运行验证。";
        }

        var nextPlan = run.Plan?.Items.FirstOrDefault(item =>
            item.Status is AgentPlanItemStatus.InProgress or AgentPlanItemStatus.Pending or AgentPlanItemStatus.Blocked);
        if (nextPlan is not null)
        {
            return $"继续计划项：{nextPlan.Title}";
        }

        return "刷新工作区状态后，从最近关键步骤继续。";
    }

    private static void AddPlanLines(AgentRun run, List<string> lines)
    {
        if (run.Plan is null || run.Plan.Items.Count == 0)
        {
            return;
        }

        var completed = run.Plan.Items
            .Where(item => item.Status == AgentPlanItemStatus.Completed)
            .Take(6)
            .Select(item => item.Title)
            .ToList();
        var remaining = run.Plan.Items
            .Where(item => item.Status is AgentPlanItemStatus.Pending or AgentPlanItemStatus.InProgress or AgentPlanItemStatus.Blocked)
            .Take(8)
            .Select(item => $"{item.Status}: {item.Title}")
            .ToList();

        if (completed.Count > 0)
        {
            lines.Add("已完成计划：" + string.Join("；", completed));
        }

        if (remaining.Count > 0)
        {
            lines.Add("未完成计划：" + string.Join("；", remaining));
        }
    }

    private static void AddChangedFiles(AgentRun run, List<string> lines)
    {
        var changedFiles = run.FileChanges
            .Select(change => change.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        if (changedFiles.Count > 0)
        {
            lines.Add("已修改文件：" + string.Join("；", changedFiles));
        }
    }

    private static void AddRecentSteps(AgentRun run, List<string> lines)
    {
        var recentSteps = run.Steps
            .OrderByDescending(step => step.Number)
            .Where(step => !string.IsNullOrWhiteSpace(step.Title))
            .Take(5)
            .Select(step => $"{step.Title}: {Truncate(step.Output, 180)}")
            .Reverse()
            .ToList();
        if (recentSteps.Count > 0)
        {
            lines.Add("最近关键步骤：" + string.Join(" | ", recentSteps));
        }
    }

    private static void AddRecentErrors(AgentRun run, List<string> lines)
    {
        var recentErrors = run.Steps
            .OrderByDescending(step => step.Number)
            .Where(step => step.IsError ||
                           step.Output.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
                           step.Output.Contains("error", StringComparison.OrdinalIgnoreCase))
            .Take(4)
            .Select(step => $"{step.Title}: {Truncate(step.Output, 220)}")
            .Reverse()
            .ToList();
        if (recentErrors.Count > 0)
        {
            lines.Add("最近错误：" + string.Join(" | ", recentErrors));
        }
    }

    private static void AddArtifactRefs(AgentRun run, List<string> lines)
    {
        var artifactRefs = run.Artifacts
            .OrderByDescending(artifact => artifact.CreatedAt)
            .Take(5)
            .Select(artifact => string.IsNullOrWhiteSpace(artifact.Summary)
                ? $"{artifact.Kind}:{artifact.Id}"
                : $"{artifact.Kind}:{Truncate(artifact.Summary, 120)}")
            .ToList();
        if (artifactRefs.Count > 0)
        {
            lines.Add("重要产物：" + string.Join("；", artifactRefs));
        }
    }

    private static string FormatVerificationCheckpoint(AgentRun run)
    {
        if (run.Verifications.Count == 0)
        {
            return "未运行";
        }

        var passed = run.Verifications.Count(verification => verification.IsSuccess);
        var failed = run.Verifications
            .Where(verification => !verification.IsSuccess)
            .Take(3)
            .Select(verification => $"{verification.Command} exit {verification.ExitCode}");
        return $"{passed}/{run.Verifications.Count} 通过" +
               (failed.Any() ? $"；失败：{string.Join("；", failed)}" : "");
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.ReplaceLineEndings(" ").Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }
}
