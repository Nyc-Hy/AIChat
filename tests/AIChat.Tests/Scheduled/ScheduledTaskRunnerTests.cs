using AIChat.Application.Scheduled;
using AIChat.Domain.Scheduled;

namespace AIChat.Tests.Scheduled;

// Tick + dispatch contract. The runner is pure logic —
// the fake registry + executor in this test fakes the
// 30s polling loop entirely.
public class ScheduledTaskRunnerTests
{
    // Minimal in-memory registry. The real one is
    // JsonFileStore-backed; we don't need disk I/O to
    // exercise the tick contract.
    private sealed class FakeRegistry : IScheduledTaskRegistry
    {
        public List<ScheduledTask> TasksList { get; } = [];
        public List<ScheduledTaskRun> RunsList { get; } = [];
        public event EventHandler? Changed;

        public IReadOnlyList<ScheduledTask> Tasks => TasksList;
        public IReadOnlyList<ScheduledTaskRun> Runs => RunsList;

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task<string> AddAsync(ScheduledTask task, CancellationToken cancellationToken = default)
        {
            task.Id = string.IsNullOrEmpty(task.Id) ? Guid.NewGuid().ToString("N") : task.Id;
            TasksList.Add(task);
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(task.Id);
        }

        public Task<bool> UpdateAsync(ScheduledTask task, CancellationToken cancellationToken = default)
        {
            var i = TasksList.FindIndex(t => t.Id == task.Id);
            if (i < 0) return Task.FromResult(false);
            TasksList[i] = task;
            return Task.FromResult(true);
        }

        public Task<bool> RemoveAsync(string taskId, CancellationToken cancellationToken = default)
        {
            var i = TasksList.FindIndex(t => t.Id == taskId);
            if (i < 0) return Task.FromResult(false);
            TasksList.RemoveAt(i);
            RunsList.RemoveAll(r => r.ScheduledTaskId == taskId);
            return Task.FromResult(true);
        }

        public Task<bool> SetPausedAsync(string taskId, bool isPaused, CancellationToken cancellationToken = default)
        {
            var i = TasksList.FindIndex(t => t.Id == taskId);
            if (i < 0) return Task.FromResult(false);
            TasksList[i].IsPaused = isPaused;
            return Task.FromResult(true);
        }

        public Task<string> RecordRunAsync(ScheduledTaskRun run, CancellationToken cancellationToken = default)
        {
            run.Id = string.IsNullOrEmpty(run.Id) ? Guid.NewGuid().ToString("N") : run.Id;
            RunsList.Add(run);
            var i = TasksList.FindIndex(t => t.Id == run.ScheduledTaskId);
            if (i >= 0) TasksList[i].LastRunAt = run.StartedAt;
            return Task.FromResult(run.Id);
        }
    }

    private sealed class CapturingExecutor : IScheduledTaskExecutor
    {
        public List<ScheduledTask> Calls { get; } = [];
        public Func<ScheduledTask, ScheduledTaskRun>? Override { get; set; }

        public Task<ScheduledTaskRun> ExecuteAsync(ScheduledTask task, CancellationToken cancellationToken = default)
        {
            Calls.Add(task);
            var run = Override?.Invoke(task) ?? new ScheduledTaskRun
            {
                ScheduledTaskId = task.Id,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Status = ScheduledRunStatus.Completed,
                Output = "fake-run",
            };
            return Task.FromResult(run);
        }
    }

    private static DateTimeOffset FixedNow(int hour = 10) =>
        new(2026, 8, 3, hour, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task TickAsync_NoTasks_FiresNothing()
    {
        var registry = new FakeRegistry();
        var executor = new CapturingExecutor();
        var runner = new ScheduledTaskRunner(registry, executor, () => FixedNow());

        var fired = await runner.TickAsync();

        Assert.Equal(0, fired);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task TickAsync_OnceDue_FiresAndRecords()
    {
        var registry = new FakeRegistry();
        var executor = new CapturingExecutor();
        // Once cadence, no LastRunAt — the runner
        // returns now() as the next-run time, so any
        // "now" is due.
        var task = new ScheduledTask
        {
            Id = "t1",
            Cadence = ScheduledCadence.Once,
            CreatedAt = FixedNow(9),
        };
        await registry.AddAsync(task);
        var runner = new ScheduledTaskRunner(registry, executor, () => FixedNow(10));

        var fired = await runner.TickAsync();

        Assert.Equal(1, fired);
        Assert.Single(executor.Calls);
        Assert.Single(registry.RunsList);
        Assert.Equal(ScheduledRunStatus.Completed, registry.RunsList[0].Status);
        Assert.Equal("t1", registry.RunsList[0].ScheduledTaskId);
    }

    [Fact]
    public async Task TickAsync_PausedTask_Skipped()
    {
        var registry = new FakeRegistry();
        var executor = new CapturingExecutor();
        // Once cadence (always due) + IsPaused → the
        // runner skips regardless.
        await registry.AddAsync(new ScheduledTask
        {
            Id = "paused",
            Cadence = ScheduledCadence.Once,
            CreatedAt = FixedNow(9),
            IsPaused = true,
        });
        var runner = new ScheduledTaskRunner(registry, executor, () => FixedNow(10));

        var fired = await runner.TickAsync();

        Assert.Equal(0, fired);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task TickAsync_NotYetDue_Skipped()
    {
        var registry = new FakeRegistry();
        var executor = new CapturingExecutor();
        // Daily 09:00; now 08:00 — today's slot is
        // still in the future, runner skips.
        await registry.AddAsync(new ScheduledTask
        {
            Id = "future",
            Cadence = ScheduledCadence.Daily,
            CadenceTime = "09:00",
        });
        var runner = new ScheduledTaskRunner(registry, executor, () => FixedNow(8));

        var fired = await runner.TickAsync();

        Assert.Equal(0, fired);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task TickAsync_ExecutorThrows_RecordsFailed_ContinuesOtherTasks()
    {
        var registry = new FakeRegistry();
        // Override throws for "boom" only — "ok" still
        // gets a Completed run, which is what proves
        // the runner didn't abort on the first
        // exception.
        var executor = new CapturingExecutor
        {
            Override = task => task.Id == "boom"
                ? throw new InvalidOperationException("agent crashed")
                : new ScheduledTaskRun
                {
                    ScheduledTaskId = task.Id,
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Status = ScheduledRunStatus.Completed,
                    Output = "ok-run",
                },
        };
        // Once cadence (always due) for both.
        await registry.AddAsync(new ScheduledTask { Id = "boom", Cadence = ScheduledCadence.Once, CreatedAt = FixedNow(8) });
        await registry.AddAsync(new ScheduledTask { Id = "ok",   Cadence = ScheduledCadence.Once, CreatedAt = FixedNow(8) });
        var runner = new ScheduledTaskRunner(registry, executor, () => FixedNow(10));

        var fired = await runner.TickAsync();

        // Both tasks were attempted; one recorded as
        // Failed, the other as Completed. The runner
        // doesn't stop on the first exception.
        Assert.Equal(2, executor.Calls.Count);
        Assert.Equal(2, registry.RunsList.Count);
        Assert.Contains(registry.RunsList, r => r.ScheduledTaskId == "boom" && r.Status == ScheduledRunStatus.Failed);
        Assert.Contains(registry.RunsList, r => r.ScheduledTaskId == "ok"   && r.Status == ScheduledRunStatus.Completed);
    }

    [Fact]
    public async Task TickAsync_MultipleDue_FiresInOrder()
    {
        var registry = new FakeRegistry();
        var executor = new CapturingExecutor();
        // Use Once cadence so the runner doesn't need
        // to time-walk; all three are due at the same
        // tick.
        await registry.AddAsync(new ScheduledTask { Id = "a", Cadence = ScheduledCadence.Once, CreatedAt = FixedNow(8) });
        await registry.AddAsync(new ScheduledTask { Id = "b", Cadence = ScheduledCadence.Once, CreatedAt = FixedNow(8) });
        await registry.AddAsync(new ScheduledTask { Id = "c", Cadence = ScheduledCadence.Once, CreatedAt = FixedNow(8) });
        var runner = new ScheduledTaskRunner(registry, executor, () => FixedNow(10));

        var fired = await runner.TickAsync();

        Assert.Equal(3, fired);
        Assert.Equal(new[] { "a", "b", "c" }, executor.Calls.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task TickAsync_AfterRun_LastRunAtFlips()
    {
        var registry = new FakeRegistry();
        var executor = new CapturingExecutor();
        var now = FixedNow(10);
        var task = new ScheduledTask
        {
            Id = "track",
            Cadence = ScheduledCadence.Once,
            CreatedAt = FixedNow(9),
        };
        await registry.AddAsync(task);
        var runner = new ScheduledTaskRunner(registry, executor, () => now);

        await runner.TickAsync();

        // The executor returns StartedAt = now; the
        // registry's RecordRunAsync bumps the parent
        // task's LastRunAt to the same value. This
        // drives the "上次 / 下次" column in the
        // Scheduled modal.
        Assert.NotNull(task.LastRunAt);
    }
}
