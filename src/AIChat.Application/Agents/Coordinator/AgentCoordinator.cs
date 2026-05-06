using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.Coordinator;

public sealed class AgentCoordinator
{
    public AgentPhaseTransition StartPhase(AgentRun run, AgentRunPhase phase, string summary = "")
    {
        var key = ToPhaseKey(phase);
        if (string.Equals(run.Phase, key, StringComparison.OrdinalIgnoreCase) &&
            run.PhaseHistory.LastOrDefault()?.Status == "running")
        {
            run.CurrentPhaseSummary = summary;
            var active = run.PhaseHistory.Last();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                active.Summary = summary;
            }

            return new AgentPhaseTransition(phase, key, "running", summary);
        }

        CompleteActivePhase(run, "completed", run.CurrentPhaseSummary);
        run.Phase = key;
        run.CurrentPhaseSummary = summary;
        run.PhaseHistory.Add(new AgentRunPhaseRecord
        {
            RunId = run.Id,
            Phase = key,
            Status = "running",
            Summary = summary,
            StartedAt = DateTimeOffset.Now
        });
        return new AgentPhaseTransition(phase, key, "running", summary);
    }

    public AgentPhaseTransition CompletePhase(AgentRun run, AgentRunPhase phase, string summary = "")
    {
        var key = ToPhaseKey(phase);
        if (!string.Equals(run.Phase, key, StringComparison.OrdinalIgnoreCase))
        {
            StartPhase(run, phase, summary);
        }

        CompleteActivePhase(run, "completed", summary);
        run.Phase = key;
        run.CurrentPhaseSummary = summary;
        return new AgentPhaseTransition(phase, key, "completed", summary);
    }

    public AgentPhaseTransition CompleteRun(AgentRun run, AgentRunStatus status, string summary = "")
    {
        var phase = status switch
        {
            AgentRunStatus.Cancelled => AgentRunPhase.Cancelled,
            AgentRunStatus.Failed => AgentRunPhase.Failed,
            _ => AgentRunPhase.Completed
        };
        var terminalStatus = status switch
        {
            AgentRunStatus.Cancelled => "cancelled",
            AgentRunStatus.Failed => "failed",
            _ => "completed"
        };

        CompleteActivePhase(run, terminalStatus, summary);
        run.Complete(status, completionReason: summary);
        run.CurrentPhaseSummary = summary;
        return new AgentPhaseTransition(phase, ToPhaseKey(phase), terminalStatus, summary);
    }

    public static AgentRunPhase ClassifyToolPhase(string toolName)
    {
        return toolName switch
        {
            "list_files" or "read_file" or "search_text" or "git_status" or "git_diff" => AgentRunPhase.GatheringContext,
            "write_file" or "edit_file" or "apply_patch" or "git_restore_file" or "git_commit" => AgentRunPhase.Executing,
            "run_build" or "run_test" => AgentRunPhase.Verifying,
            "update_plan" => AgentRunPhase.Planning,
            _ => AgentRunPhase.Executing
        };
    }

    public static string ToPhaseKey(AgentRunPhase phase)
    {
        return phase switch
        {
            AgentRunPhase.Planning => "planning",
            AgentRunPhase.GatheringContext => "gathering_context",
            AgentRunPhase.Executing => "executing",
            AgentRunPhase.Verifying => "verifying",
            AgentRunPhase.Repairing => "repairing",
            AgentRunPhase.Summarizing => "summarizing",
            AgentRunPhase.WaitingForUser => "waiting_for_user",
            AgentRunPhase.Failed => "failed",
            AgentRunPhase.Cancelled => "cancelled",
            _ => "completed"
        };
    }

    private static void CompleteActivePhase(AgentRun run, string status, string summary)
    {
        var active = run.PhaseHistory.LastOrDefault(record => record.Status == "running");
        if (active is null)
        {
            return;
        }

        active.Status = status;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            active.Summary = summary;
        }

        active.CompletedAt = DateTimeOffset.Now;
    }
}
