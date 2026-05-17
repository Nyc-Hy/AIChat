using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.Benchmark;

public sealed class AgentBenchmarkEvaluator
{
    public AgentBenchmarkResult Evaluate(AgentBenchmarkTask task, AgentRun run)
    {
        var telemetry = AgentRunTelemetryBuilder.Build(run);
        var outcome = AgentRunTelemetryBuilder.ClassifyOutcome(run);
        var failures = new List<string>();

        if (outcome != AgentRunOutcomeKind.Success)
        {
            failures.Add($"outcome={outcome}");
        }

        if (run.QualityScore < 70)
        {
            failures.Add($"quality={run.QualityScore}");
        }

        if (task.RequiresMutation && !run.MutationToolSucceeded)
        {
            failures.Add("mutation-not-recorded");
        }

        if (task.RequiresVerification && telemetry.VerificationSuccessCount == 0)
        {
            failures.Add("verification-missing");
        }

        if (telemetry.ToolCallCount > task.MaxToolCalls)
        {
            failures.Add($"tool-budget={telemetry.ToolCallCount}/{task.MaxToolCalls}");
        }

        if (telemetry.EstimatedPromptTokens > task.MaxEstimatedPromptTokens)
        {
            failures.Add($"prompt-budget={telemetry.EstimatedPromptTokens}/{task.MaxEstimatedPromptTokens}");
        }

        return new AgentBenchmarkResult(
            task.Id,
            task.Name,
            failures.Count == 0,
            outcome,
            run.QualityScore,
            telemetry.ToolCallCount,
            telemetry.EstimatedPromptTokens,
            failures.Count == 0 ? "passed" : string.Join("; ", failures));
    }
}
