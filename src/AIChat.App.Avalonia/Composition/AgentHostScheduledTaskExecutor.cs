using System.Threading.Tasks;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Scheduled;
using AIChat.Domain.Scheduled;

namespace AIChat.App.Avalonia.Composition;

// Concrete IScheduledTaskExecutor that routes a due
// ScheduledTask through AgentHost.SendTaskAsync — the same
// path the ⌘↵ keyboard shortcut and the composer send
// button use. The scheduler doesn't know about Avalonia or
// MVVM; it just sees this interface and trusts the
// implementation to do the right thing.
//
// Approval-on-no-human-interaction (plan §7 Wave 9): when
// a scheduled run lands on a tool that requires approval
// and the user is not at the app, the existing
// tool-approval modal would sit on screen until the user
// returns. The executor watches the agent's status; if
// "需要审批" lasts longer than UnattendedApprovalTimeout
// (default 30s), it auto-rejects the pending request via
// IApprovalService.RejectPendingIfAny. The tool call
// errors out, the run ends, and the executor records the
// run as Failed with a clear "auto-rejected (无人值守
// timeout)" message. The user sees the timeout reason
// in the run history the next time they open the
// Scheduled modal.
public sealed class AgentHostScheduledTaskExecutor : IScheduledTaskExecutor
{
    // 30s: the user isn't expected to be glued to the
    // window for a scheduled run, but a 5s timeout
    // would race the "modal just opened" state. 30s
    // gives the user time to glance at the dialog
    // when they happen to be at the app, and the
    // run still completes (with a Failed run
    // record) within a single scheduler tick on
    // the unattended path.
    private static readonly TimeSpan UnattendedApprovalTimeout = TimeSpan.FromSeconds(30);

    // Poll interval for IsRunning / status checks.
    // 500ms keeps the loop responsive to the user's
    // Approve / Reject clicks (sub-second feedback)
    // while staying well under the unattended-
    // timeout budget so the timeout fires on
    // schedule even on a busy CI box.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly AgentHostViewModel _agentHost;
    private readonly ISettingsHolder _settings;
    private readonly IApprovalService _approval;
    private readonly Func<DateTimeOffset> _now;

    public AgentHostScheduledTaskExecutor(
        AgentHostViewModel agentHost,
        ISettingsHolder settings,
        IApprovalService approval,
        Func<DateTimeOffset>? now = null)
    {
        _agentHost = agentHost ?? throw new ArgumentNullException(nameof(agentHost));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _approval = approval ?? throw new ArgumentNullException(nameof(approval));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<ScheduledTaskRun> ExecuteAsync(
        ScheduledTask task,
        CancellationToken cancellationToken = default)
    {
        var startedAt = _now();

        // Already running — defer to next tick. We
        // record the run as Cancelled so the user
        // sees the scheduler tried and was
        // preempted; the next pass (30s later by
        // default) re-fires.
        if (_agentHost.IsRunning)
        {
            return new ScheduledTaskRun
            {
                ScheduledTaskId = task.Id,
                StartedAt = startedAt,
                CompletedAt = _now(),
                Status = ScheduledRunStatus.Cancelled,
                Output = "",
                ErrorMessage = "Agent is already running — deferred to next tick.",
            };
        }

        // Promote the scheduled prompt into the
        // composer and fire SendTask. SendTask reads
        // DraftPrompt, runs the agent, and clears
        // DraftPrompt on its way out.
        _agentHost.DraftPrompt = task.Prompt;

        _ = _agentHost.SendTaskCommand.ExecuteAsync(null);

        // Poll IsRunning / status until the run lands
        // OR the cancellation token fires. Inside
        // the loop we track whether the run stopped
        // at an approval gate so the outer code can
        // record the run with the right status (the
        // "user came back and approved" case is a
        // normal Completed run; the "we auto-rejected"
        // case is a Failed run with a clear message).
        var wasAutoRejected = false;
        var approvalDeadline = DateTimeOffset.MinValue;
        while (_agentHost.IsRunning && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);

            // Approval gate: the agent has stopped
            // at a tool that needs human sign-off.
            // IsRunning flips false the moment the
            // agent pauses for the approval, so this
            // check fires before the IsRunning check
            // breaks us out of the loop. We start a
            // deadline the first time we see the
            // status and fire RejectPendingIfAny on
            // timeout. The user (if present) can
            // still approve / deny in the modal —
            // either flips IsRunning back to true as
            // the agent resumes, and the next poll
            // tick breaks us out of the approval-wait
            // branch.
            if (string.Equals(_agentHost.LastAssistantStatus, "需要审批", StringComparison.Ordinal))
            {
                if (approvalDeadline == DateTimeOffset.MinValue)
                {
                    approvalDeadline = _now() + UnattendedApprovalTimeout;
                }
                else if (_now() >= approvalDeadline)
                {
                    // Auto-reject so the run doesn't
                    // strand on the approval modal
                    // forever. The tool call errors
                    // out, the agent's run ends, and
                    // the loop's IsRunning check
                    // exits on the next poll. The
                    // user sees the timeout reason
                    // in the run history the next
                    // time they open the modal.
                    _approval.RejectPendingIfAny(
                        "auto-rejected (无人值守 timeout)");
                    wasAutoRejected = true;
                    approvalDeadline = DateTimeOffset.MaxValue;
                }
            }
            else
            {
                // Status moved off "需要审批" — the
                // user either approved or denied, so
                // the agent is running again (or
                // finished). Reset the deadline so a
                // later approval in the same run
                // gets its own fresh timeout.
                approvalDeadline = DateTimeOffset.MinValue;
            }
        }

        var completedAt = _now();

        // Auto-rejected: the agent's LastAssistant
        // Status will be "已完成" (the rejection is
        // not a run error from the agent's
        // perspective — the tool just didn't run),
        // so we override the status to Failed with
        // a clear "无人值守 timeout" message. The
        // user can see at a glance why the run
        // didn't actually do anything.
        if (wasAutoRejected)
        {
            return new ScheduledTaskRun
            {
                ScheduledTaskId = task.Id,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                Status = ScheduledRunStatus.Failed,
                Output = "",
                ErrorMessage = "工具审批无人值守 timeout,自动拒绝。",
            };
        }

        var status = MapAgentStatus(_agentHost.LastAssistantStatus);
        return new ScheduledTaskRun
        {
            ScheduledTaskId = task.Id,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Status = status,
            Output = "",
            ErrorMessage = status == ScheduledRunStatus.Failed
                ? _agentHost.LastAssistantStatus
                : null,
        };
    }

    private static ScheduledRunStatus MapAgentStatus(string? lastStatus)
    {
        if (string.IsNullOrWhiteSpace(lastStatus))
        {
            return ScheduledRunStatus.Completed;
        }
        if (string.Equals(lastStatus, "需要审批", StringComparison.Ordinal))
        {
            return ScheduledRunStatus.ApprovalRequired;
        }
        if (string.Equals(lastStatus, "已停止", StringComparison.Ordinal))
        {
            return ScheduledRunStatus.Cancelled;
        }
        if (string.Equals(lastStatus, "失败", StringComparison.Ordinal))
        {
            return ScheduledRunStatus.Failed;
        }
        return ScheduledRunStatus.Completed;
    }
}
