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
// Approval-on-no-human-interaction (plan §7 Wave 9): the
// first slice is conservative. If a scheduled run lands
// on a tool requiring approval and the user is not at the
// app, the existing tool-approval modal sits on screen
// until the user returns (the agent's IsRunning flips
// false with LastAssistantStatus = "需要审批" so the
// scheduler records an ApprovalRequired run and the user
// sees the prompt when they come back). A follow-up
// slice will add an unattended-timeout that auto-denies
// after N seconds — for now the user is expected to
// either approve, deny, or stop the app before the next
// tick. The next-tick behaviour is also conservative:
// if the agent is already running (user or another
// scheduled task), the scheduler skips this tick and
// retries on the next pass.
public sealed class AgentHostScheduledTaskExecutor : IScheduledTaskExecutor
{
    private readonly AgentHostViewModel _agentHost;
    private readonly ISettingsHolder _settings;
    private readonly Func<DateTimeOffset> _now;

    public AgentHostScheduledTaskExecutor(
        AgentHostViewModel agentHost,
        ISettingsHolder settings,
        Func<DateTimeOffset>? now = null)
    {
        _agentHost = agentHost ?? throw new ArgumentNullException(nameof(agentHost));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _now = now ?? (() => DateTimeOffset.Now);
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

        // No-write mode: scheduled tasks that the
        // user previously left in read-only mode
        // should still run in read-only — the
        // _agentHost.SendTaskAsync reads the current
        // NoWriteMode flag at fire time. We don't
        // need to do anything here.
        _ = _settings;

        // Promote the scheduled prompt into the
        // composer and fire SendTask. SendTask reads
        // DraftPrompt, runs the agent, and clears
        // DraftPrompt on its way out. We then wait
        // for IsRunning to flip false (= the run
        // finished) and translate the agent status
        // into a ScheduledRunStatus.
        _agentHost.DraftPrompt = task.Prompt;

        var sendTask = _agentHost.SendTaskCommand
            .ExecuteAsync(null);

        // Poll IsRunning instead of awaiting the
        // command — the command itself returns
        // immediately, but the agent run holds
        // IsRunning = true until the run lands.
        while (_agentHost.IsRunning && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        // Map the agent's status string to the
        // scheduled run's status enum. The mapping
        // is the single source of truth for "what
        // does the agent think happened"; the rest
        // of the system reads the enum.
        var status = MapAgentStatus(_agentHost.LastAssistantStatus);
        var completedAt = _now();
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
