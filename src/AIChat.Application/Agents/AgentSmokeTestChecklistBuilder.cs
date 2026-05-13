using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

public static class AgentSmokeTestChecklistBuilder
{
    public static IReadOnlyList<AgentSmokeTestItem> Build(AgentRun run)
    {
        var items = new List<AgentSmokeTestItem>
        {
            BuildGoalItem(run),
            BuildChangeScopeItem(run),
            BuildVerificationItem(run)
        };

        var riskItem = BuildRiskItem(run);
        if (riskItem is not null)
        {
            items.Add(riskItem);
        }

        var artifactItem = BuildArtifactItem(run);
        if (artifactItem is not null)
        {
            items.Add(artifactItem);
        }

        return items.Take(5).ToList();
    }

    private static AgentSmokeTestItem BuildGoalItem(AgentRun run)
    {
        var status = run.Status == AgentRunStatus.Completed
            ? AgentSmokeTestStatus.NeedsReview
            : AgentSmokeTestStatus.Blocked;
        var detail = run.Status == AgentRunStatus.Completed
            ? Trim($"对照原始目标确认结果是否符合预期：{run.Goal}", 180)
            : $"当前状态为 {FormatStatus(run.Status)}，需先继续或重试后再验收目标。";

        return new AgentSmokeTestItem("确认目标完成度", detail, status);
    }

    private static AgentSmokeTestItem BuildChangeScopeItem(AgentRun run)
    {
        if (run.FileChanges.Count == 0)
        {
            var detail = run.MutationToolSucceeded
                ? "记录到修改工具，但未记录具体文件，请打开运行详情核对工具结果。"
                : "本轮未记录文件变更，适合只读分析、解释或排查类任务。";
            var status = run.MutationToolSucceeded ? AgentSmokeTestStatus.NeedsReview : AgentSmokeTestStatus.Passed;
            return new AgentSmokeTestItem("检查变更范围", detail, status);
        }

        var paths = run.FileChanges
            .Select(change => change.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        var suffix = run.FileChanges.Count > paths.Count ? $" 等 {run.FileChanges.Count} 个文件" : "";
        return new AgentSmokeTestItem(
            "检查变更范围",
            $"确认这些文件是本任务需要修改的范围：{string.Join(", ", paths)}{suffix}",
            AgentSmokeTestStatus.NeedsReview);
    }

    private static AgentSmokeTestItem BuildVerificationItem(AgentRun run)
    {
        if (run.Verifications.Count == 0)
        {
            var status = run.FileChanges.Count > 0 || run.MutationToolSucceeded
                ? AgentSmokeTestStatus.NeedsReview
                : AgentSmokeTestStatus.Passed;
            var detail = status == AgentSmokeTestStatus.Passed
                ? "本轮没有修改项目文件，未运行验证可以接受。"
                : "本轮有修改但未记录验证，请运行项目验证命令或做一次最小手动测试。";
            return new AgentSmokeTestItem("确认验证结果", detail, status);
        }

        var passed = run.Verifications.Count(verification => verification.IsSuccess);
        var failed = run.Verifications.Count - passed;
        var statusText = failed == 0
            ? $"{passed}/{run.Verifications.Count} 个验证通过。"
            : $"{passed}/{run.Verifications.Count} 个验证通过，{failed} 个失败。";
        var verificationStatus = failed == 0 ? AgentSmokeTestStatus.Passed : AgentSmokeTestStatus.Blocked;
        return new AgentSmokeTestItem("确认验证结果", statusText, verificationStatus);
    }

    private static AgentSmokeTestItem? BuildRiskItem(AgentRun run)
    {
        var risks = new List<string>();
        if (run.ToolBudgetExceeded || run.Status == AgentRunStatus.BudgetExceeded)
        {
            risks.Add("工具预算耗尽");
        }

        if (run.ToolApprovalRejectedCount > 0)
        {
            risks.Add($"工具被拒绝 {run.ToolApprovalRejectedCount} 次");
        }

        if (run.FinalValidationSummary.Contains("一致性风险", StringComparison.OrdinalIgnoreCase) ||
            run.FinalValidationSummary.Contains("存在风险", StringComparison.OrdinalIgnoreCase))
        {
            risks.Add("最终回复存在一致性风险");
        }

        if (risks.Count == 0)
        {
            return new AgentSmokeTestItem("检查风险信号", "未发现预算、审批或一致性风险。", AgentSmokeTestStatus.Passed);
        }

        return new AgentSmokeTestItem("检查风险信号", string.Join("；", risks), AgentSmokeTestStatus.Blocked);
    }

    private static AgentSmokeTestItem? BuildArtifactItem(AgentRun run)
    {
        if (run.Artifacts.Count == 0)
        {
            return null;
        }

        var newest = run.Artifacts
            .OrderByDescending(artifact => artifact.CreatedAt)
            .Take(3)
            .Select(artifact => string.IsNullOrWhiteSpace(artifact.ToolName)
                ? artifact.Kind
                : $"{artifact.ToolName}:{artifact.Kind}");
        return new AgentSmokeTestItem(
            "查看关键产物",
            $"打开或复制需要验收的产物：{string.Join(", ", newest)}",
            AgentSmokeTestStatus.NeedsReview);
    }

    private static string FormatStatus(AgentRunStatus status)
    {
        return status switch
        {
            AgentRunStatus.BudgetExceeded => "已暂停",
            AgentRunStatus.Cancelled => "已停止",
            AgentRunStatus.Failed => "失败",
            AgentRunStatus.Running => "运行中",
            _ => "完成"
        };
    }

    private static string Trim(string value, int maxChars)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxChars ? normalized : normalized[..maxChars] + "...";
    }
}
