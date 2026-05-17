namespace AIChat.App.ViewModels;

public static class AgentRunReviewPacketBuilder
{
    public static string BuildRunSummary(AgentRunViewModel run)
    {
        var lines = new List<string>
        {
            $"状态：{run.StatusText}",
            $"阶段：{run.PhaseText}",
            $"目标：{run.Goal}",
            $"项目：{run.ProjectPath}",
            $"模型：{run.Model}",
            $"工具：{run.EnabledToolCount}",
            $"预算：{run.BudgetText}",
            $"修改记录：{run.MutationGuardrailText}",
            $"审批：{run.ApprovalSummary}",
            $"工作区：{run.WorkspaceSnapshotText}",
            $"指标：{run.MetricsSummary.ReplaceLineEndings(" · ")}",
            $"Telemetry：{run.TelemetrySummary.ReplaceLineEndings(" · ")}",
            run.BenchmarkSummary,
            $"质量：{run.QualityScoreText} · {run.QualitySummary}",
            $"策略建议：{run.StrategySuggestion}",
            $"验收：{run.SmokeTestSummary}",
            $"用户验收：{run.AcceptanceStatusText} · {run.AcceptanceNote}",
            $"调试：{run.ExecutionModeText} · {run.TaskComplexityText} · Planner {run.PlannerUsageText} · Explorer {run.ExplorerUsageText}",
            $"计划：{run.PlanSummary}",
            $"步骤：{run.StepCount}",
            $"文件变更：{run.FileChangeCount}",
            $"验证：{run.VerificationCount}",
            $"产物：{run.ArtifactCount}",
            $"耗时：{run.DurationText}"
        };

        if (run.HasContinuation)
        {
            lines.Add($"继续自：{run.ContinuedFromRunId}");
        }

        if (run.HasRetrySource)
        {
            lines.Add($"重试自：{run.RetriedFromRunId}");
        }

        if (run.HasCompletionReason)
        {
            lines.Add($"原因：{run.CompletionReasonText}");
        }

        if (run.HasFinalValidationSummary)
        {
            lines.Add("");
            lines.Add("结束校验：");
            lines.Add(run.FinalValidationSummary);
        }

        lines.Add("");
        lines.Add("调试摘要：");
        lines.Add(run.DebugSummary);

        if (run.HasRecoverySuggestion)
        {
            lines.Add("");
            lines.Add("恢复建议：");
            lines.Add(run.RecoverySuggestion);
        }

        if (run.ChangedPaths.Count > 0)
        {
            lines.Add("");
            lines.Add("变更文件：");
            lines.AddRange(run.ChangedPaths.Select(path => $"- {path}"));
        }

        if (run.Verifications.Count > 0)
        {
            lines.Add("");
            lines.Add("验证结果：");
            lines.AddRange(run.Verifications.Select(item => $"- {item.Command}: {item.StatusText} ({item.ExitCodeText})"));
        }

        if (run.SmokeTests.Count > 0)
        {
            lines.Add("");
            lines.Add("验收清单：");
            lines.AddRange(run.SmokeTests.Select(item => $"- [{item.StatusText}] {item.Title}: {item.Detail}"));
        }

        if (run.Artifacts.Count > 0)
        {
            lines.Add("");
            lines.Add("产物：");
            lines.AddRange(run.Artifacts.Select(item => $"- {item.ToolName}: {item.Kind}, {item.ContentLengthText}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildReviewPacket(AgentRunViewModel run)
    {
        var lines = new List<string>
        {
            "# Agent Run Review",
            "",
            $"Status: {run.StatusText}",
            $"Goal: {run.Goal}",
            $"Model: {run.Model}",
            $"Project: {run.ProjectPath}",
            $"Started: {run.StartedText}",
            $"Duration: {run.DurationText}",
        };

        if (run.HasContinuation)
        {
            lines.Add($"ContinuedFrom: {run.ContinuedFromRunId}");
        }

        if (run.HasRetrySource)
        {
            lines.Add($"RetriedFrom: {run.RetriedFromRunId}");
        }

        lines.AddRange([
            "",
            "## Guardrails",
            $"- Budget: {run.BudgetText}",
            $"- Mutation evidence: {run.MutationGuardrailText}",
            $"- Approval: {run.ApprovalSummary}",
            $"- Workspace: {run.WorkspaceSnapshotText}",
            "",
            "## Execution Debug",
            run.MetricsSummary,
            "",
            "## Telemetry",
            run.TelemetrySummary,
            run.OutcomeReason,
            "",
            "## Benchmark",
            run.BenchmarkSummary,
            "",
            "## Quality",
            $"{run.QualityScoreText}: {run.QualitySummary}",
            run.StrategySuggestion,
            "",
            "## Smoke Test Checklist"
        ]);

        lines.AddRange(run.SmokeTests.Select(item => $"- [{item.StatusText}] {item.Title}: {item.Detail}"));

        lines.AddRange([
            "",
            "## User Acceptance",
            $"{run.AcceptanceStatusText} at {run.AcceptanceReviewedAtText}",
            run.AcceptanceNote,
            "",
            run.DebugSummary,
            "",
            "## Final Validation",
            run.FinalValidationSummary,
            "",
            "## Recovery Suggestion",
            run.RecoverySuggestion
        ]);

        if (run.Plan is not null)
        {
            lines.Add("");
            lines.Add("## Plan");
            lines.Add(run.Plan.Summary);
            lines.Add(run.Plan.ProgressText);
            foreach (var item in run.Plan.Items)
            {
                lines.Add($"- [{item.StatusText}] {item.Title}{(item.HasNotes ? $" — {item.Notes}" : "")}");
            }
        }

        if (run.HasCompletionReason)
        {
            lines.Add("");
            lines.Add("## Completion Reason");
            lines.Add(run.CompletionReasonText);
        }

        if (run.ChangedPaths.Count > 0)
        {
            lines.Add("");
            lines.Add("## Changed Files");
            lines.AddRange(run.ChangedPaths.Select(path => $"- {path}"));
        }

        if (run.Verifications.Count > 0)
        {
            lines.Add("");
            lines.Add("## Verifications");
            lines.AddRange(run.Verifications.Select(item => $"- {item.Command}: {item.StatusText} ({item.ExitCodeText})"));
        }

        if (run.Artifacts.Count > 0)
        {
            lines.Add("");
            lines.Add("## Artifacts");
            lines.AddRange(run.Artifacts.Select(item => $"- {item.ToolName}: {item.Kind}, {item.ContentLengthText}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
