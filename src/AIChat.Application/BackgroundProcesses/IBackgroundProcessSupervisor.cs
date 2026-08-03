using AIChat.Domain.BackgroundProcesses;

namespace AIChat.Application.BackgroundProcesses;

// Wave 7 follow-up: the supervisor surface the host
// (EnvironmentPanel / Sites preview / Scheduled run
// log) needs. Lives next to the implementation so the
// desktop host takes a direct dependency (same pattern
// as IPluginRegistry / IScheduledTaskRegistry /
// ISiteRegistry). Tests inject a fake via the
// InMemoryAppRepository pattern.
//
// Scope of the first slice:
//   * StartAsync / StopAsync / ListAsync / GetAsync
//   * Process-tree kill (setpgid + kill -- -pgid on
//     Unix, macOS supported; Windows uses job
//     objects — follow-up slice)
//   * Log tail capture (stdout + stderr → ring buffer
//     of last MaxLogLines)
//   * Persist to a sidecar JSON file so a restart
//     can reconcile the on-disk state with what's
//     still running
//   * Per-process cancellation via CancellationToken
//     so the host can stop a preview without leaking
//     the underlying process
public interface IBackgroundProcessSupervisor
{
    // Stable directory + file the supervisor reads /
    // writes. Surfaced in the UI for "where do these
    // processes get tracked" hints.
    string ProcessesFile { get; }

    IReadOnlyList<BackgroundProcess> Processes { get; }

    event EventHandler? Changed;

    Task ReloadAsync(CancellationToken cancellationToken = default);

    // Spawn the process. Returns the new process id.
    // The status moves Pending → Running once the OS
    // process is alive; the supervisor fires Changed
    // synchronously after the spawn returns so the UI
    // can re-bind.
    Task<string> StartAsync(
        BackgroundProcess process,
        CancellationToken cancellationToken = default);

    // Stop the process by id. Returns false if the
    // process is unknown. The kill is process-tree
    // scoped: SIGTERM to the group, escalate to
    // SIGKILL if the process doesn't exit within
    // killTimeout.
    Task<bool> StopAsync(
        string processId,
        TimeSpan? killTimeout = null,
        CancellationToken cancellationToken = default);

    // 2026-08-03: stop every running process at app shutdown.
    // Returns the count of processes that were signalled. The
    // desktop host calls this from `desktop.Exit` before disposing
    // the DI container so a user closing the window does not leak
    // a `python3 -m http.server` (or any other supervised child)
    // as an orphaned process group. Best-effort: per-process
    // failures are swallowed and reported via the count, never
    // thrown, because the host is already on the shutdown path.
    Task<int> StopAllAsync(
        TimeSpan? killTimeout = null,
        CancellationToken cancellationToken = default);
}
