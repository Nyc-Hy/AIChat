using AIChat.Domain.Scheduled;

namespace AIChat.Application.Scheduled;

// Cron-equivalent runner. Scans the registry for due
// tasks, fires them through the executor, records the
// result. Lives in AIChat.Application so it's testable
// without a live UI / AgentHost — the executor is the
// seam.
//
// Threading: TickAsync is a single pass that runs the
// scan + dispatch sequentially. The host (a
// PeriodicTimer in the App layer) decides the cadence.
// Inside TickAsync, fires are awaited one at a time so
// the runner doesn't accidentally fan out N agent runs
// in parallel when 5 tasks come due simultaneously. A
// follow-up slice that needs parallelism can add a
// bounded-channel dispatch.
public sealed class ScheduledTaskRunner
{
    private readonly IScheduledTaskRegistry _registry;
    private readonly IScheduledTaskExecutor _executor;
    private readonly Func<DateTimeOffset> _now;

    public ScheduledTaskRunner(
        IScheduledTaskRegistry registry,
        IScheduledTaskExecutor executor,
        Func<DateTimeOffset>? now = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _now = now ?? (() => DateTimeOffset.Now);
    }

    // One pass: find every non-paused task whose
    // NextRunAt is in the past, fire it through the
    // executor, and persist the resulting run record.
    // Returns the number of tasks that fired this pass
    // (useful for the host's "X tasks ran this tick"
    // toast).
    public async Task<int> TickAsync(CancellationToken cancellationToken = default)
    {
        var now = _now();
        var due = _registry.Tasks
            .Where(task => !task.IsPaused)
            .Select(task => (Task: task, Next: ScheduledTaskCadence.NextRunAt(task, now)))
            .Where(pair => pair.Next is not null && pair.Next <= now)
            .ToList();

        if (due.Count == 0)
        {
            return 0;
        }

        var fired = 0;
        foreach (var (task, _) in due)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            try
            {
                var run = await _executor
                    .ExecuteAsync(task, cancellationToken)
                    .ConfigureAwait(false);
                await _registry
                    .RecordRunAsync(run, cancellationToken)
                    .ConfigureAwait(false);
                fired++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A single bad task shouldn't kill the
                // whole tick. Persist a Failed run so
                // the user sees the problem in the
                // history view, and move on.
                var failureRun = new ScheduledTaskRun
                {
                    ScheduledTaskId = task.Id,
                    StartedAt = _now(),
                    CompletedAt = _now(),
                    Status = ScheduledRunStatus.Failed,
                    Output = "",
                    ErrorMessage = ex.Message,
                };
                try
                {
                    await _registry.RecordRunAsync(failureRun, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort: if even the failure
                    // record can't be persisted, swallow
                    // — the next tick will retry.
                }
            }
        }

        return fired;
    }
}
