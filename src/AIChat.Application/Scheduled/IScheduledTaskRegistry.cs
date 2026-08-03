using AIChat.Domain.Scheduled;

namespace AIChat.Application.Scheduled;

// Wave 9 (parity plan §7 Wave 9): the registry surface
// the host (DI / ScheduledView / agent runner) needs from
// the Scheduled system. Lives next to the implementation
// (same pattern as IPluginRegistry) so the desktop host
// takes a direct dependency on
// AIChat.Application.Scheduled.
//
// Scope of the first slice:
//   * load + persist a list of ScheduledTask rows
//   * load + persist a list of ScheduledTaskRun rows
//   * add / update / remove a task
//   * pause / resume a task (IsPaused flip)
//   * record a run (append ScheduledTaskRun + flip
//     task.LastRunAt) so the history view stays live
//
// Out of scope (follow-up slices):
//   * the cron / scheduler engine that actually fires
//     these on a timer
//   * approval-on-no-human-interaction (lives in
//     AgentRunnerViewModel; this registry just stores
//     the resulting ScheduledTaskRun)
public interface IScheduledTaskRegistry
{
    IReadOnlyList<ScheduledTask> Tasks { get; }
    IReadOnlyList<ScheduledTaskRun> Runs { get; }

    event EventHandler? Changed;

    Task ReloadAsync(CancellationToken cancellationToken = default);

    // Add a new task. Persists immediately. Returns the
    // assigned id (the same as task.Id) so callers can
    // chain a follow-up "立即运行" without re-reading the
    // list.
    Task<string> AddAsync(ScheduledTask task, CancellationToken cancellationToken = default);

    // Replace an existing task by id. Returns false if
    // the id is unknown (the caller dropped the row from
    // another window — treat as no-op rather than crash).
    Task<bool> UpdateAsync(ScheduledTask task, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string taskId, CancellationToken cancellationToken = default);

    // Flip the IsPaused flag. The actual scheduler engine
    // (follow-up slice) reads this flag before deciding
    // whether to fire; for now it's a UI-only state.
    Task<bool> SetPausedAsync(string taskId, bool isPaused, CancellationToken cancellationToken = default);

    // Append a run record + flip task.LastRunAt. Returns
    // the assigned run id. Pass-through to RecordRunAsync
    // for the actual write — the convenience method
    // also bumps the parent task's last-run timestamp so
    // the "上次 / 下次" column stays accurate.
    Task<string> RecordRunAsync(ScheduledTaskRun run, CancellationToken cancellationToken = default);
}
