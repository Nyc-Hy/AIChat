using AIChat.Application.Scheduled;

namespace AIChat.App.Avalonia.Composition;

// Drives the ScheduledTaskRunner on a PeriodicTimer. The
// app starts this on UI init and stops it on shutdown —
// the same lifetime as the desktop window. A follow-up
// slice that wants out-of-process scheduling (so a run
// fires even when the app is closed) would replace this
// with a system-cron / launchd / Task Scheduler entry
// that pokes a small CLI; for the 1.0.1 first slice the
// desktop-only model is the right shape — the user opens
// the app, leaves it open, and the tick fires every
// 30 seconds.
//
// Tick interval: 30s. Daily / weekly cadences don't need
// sub-minute precision; the on-tick evaluation walks the
// task list anyway. A smaller interval (say 5s) would
// burn CPU for no user-visible difference.
public sealed class SchedulerHostedService : IAsyncDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly ScheduledTaskRunner _runner;
    private readonly Func<DateTimeOffset> _now;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private int _disposed;

    public SchedulerHostedService(
        ScheduledTaskRunner runner,
        Func<DateTimeOffset>? now = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public TimeSpan TickIntervalValue => TickInterval;

    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }
        _loopTask = Task.Run(() => LoopAsync(_cts.Token));
    }

    // PeriodicTimer is the .NET 6+ "wait N seconds
    // unless cancelled" primitive. The loop survives
    // TickAsync throwing (it logs + continues) so a
    // single bad task can't take the scheduler down.
    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _runner.TickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Swallow + keep ticking. The runner
                // already persists a Failed run for
                // any per-task throw; loop-level
                // exceptions (e.g. registry disk
                // error) shouldn't kill the
                // scheduler.
            }

            try
            {
                await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }
        _cts.Cancel();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch
            {
                // Shutdown path: never let cleanup throw.
            }
        }
        _cts.Dispose();
    }
}
