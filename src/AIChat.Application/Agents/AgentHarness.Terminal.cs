using AIChat.Application.Security;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

// Terminal-event sequence — split out from the main
// AgentHarness partial so the orchestration file stays focused
// on the run loop. EmitTerminalAsync is the shared three-event
// sequence every terminal state (Cancelled / BudgetExceeded /
// the inner BudgetExceeded branch of Completed) needs:
// ContentDelta with the user-visible message, PhaseChanged
// for the final phase, and RunCompleted with the final step.
// Pulled out because the three call sites in
// RunToolLoopPhaseAsync and the one in the BudgetExceeded
// branch of Completed all had the exact same body in the
// pre-extraction file.
public sealed partial class AgentHarness
{
    // Shared terminal-event sequence used by both the Cancelled
    // and BudgetExceeded switch arms, and by the BudgetExceeded
    // branch inside the Completed arm. Emits the three events every
    // terminal state needs: ContentDelta (with the user-visible
    // message), PhaseChanged (the final phase), RunCompleted (the
    // final step). The two paths differed only in (message, step
    // title, status, and whether run.ToolBudgetExceeded /
    // run.CompletionReason were pre-set), so the helper takes
    // those four values and does the bookkeeping once. Cancelled
    // runs redact the user-supplied reason; budget-exceeded runs
    // set the canonical "tool budget" message themselves.
    private async IAsyncEnumerable<AgentHarnessEvent> EmitTerminalAsync(
        AgentRun run,
        string message,
        string stepTitle,
        AgentRunStatus status,
        bool isBudgetExceeded)
    {
        if (isBudgetExceeded)
        {
            run.ToolBudgetExceeded = true;
            run.CompletionReason = "已达到工具调用轮数上限。";
        }
        else
        {
            run.CompletionReason = SensitiveDataRedactor.RedactText(message);
        }

        yield return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.ContentDelta,
            Run = run,
            Content = message
        };
        yield return CreatePhaseChanged(run, CompleteRun(run, status));
        var step = AddCompletedStep(
            run,
            run.Steps.Count + 1,
            AgentStepType.Final,
            stepTitle,
            "",
            message);
        yield return new AgentHarnessEvent
        {
            Type = AgentHarnessEventType.RunCompleted,
            Run = run,
            Step = step
        };
        // IAsyncEnumerable must yield — keep the compiler quiet
        // even when this phase is the terminal one.
        await Task.CompletedTask;
    }
}
