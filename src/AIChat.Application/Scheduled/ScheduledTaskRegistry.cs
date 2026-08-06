using AIChat.Abstractions.Configuration;
using AIChat.Application.Persistence;
using AIChat.Domain.Scheduled;

namespace AIChat.Application.Scheduled;

// Concrete IScheduledTaskRegistry. Stores tasks and runs
// as separate JSON files under the app data directory
// (default locations in AppRuntimeProfile; tests can
// pass a custom directory via the ctor).
//
// Concurrency: a single `_gate` serialises read-modify-
// write sections. Saves are atomic via JsonFileStore's
// temp-file + move pattern. Reads outside the lock are
// safe (the list reference is replaced atomically) but
// consumers should re-read on `Changed` if they want
// strict consistency.
public sealed class ScheduledTaskRegistry : IScheduledTaskRegistry
{
    private readonly string _tasksFilePath;
    private readonly string _runsFilePath;
    private readonly object _gate = new();

    private List<ScheduledTask> _tasks = [];
    private List<ScheduledTaskRun> _runs = [];

    public ScheduledTaskRegistry(string? tasksFilePath = null, string? runsFilePath = null)
    {
        _tasksFilePath = tasksFilePath ?? AppRuntimeProfile.ScheduledTasksFile;
        _runsFilePath = runsFilePath ?? AppRuntimeProfile.ScheduledTaskRunsFile;
    }

    public IReadOnlyList<ScheduledTask> Tasks
    {
        get { lock (_gate) { return _tasks.ToArray(); } }
    }

    public IReadOnlyList<ScheduledTaskRun> Runs
    {
        get { lock (_gate) { return _runs.ToArray(); } }
    }

    public event EventHandler? Changed;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var tasks = await JsonFileStore
            .LoadListAsync<ScheduledTask>(_tasksFilePath, cancellationToken)
            .ConfigureAwait(false);
        var runs = await JsonFileStore
            .LoadListAsync<ScheduledTaskRun>(_runsFilePath, cancellationToken)
            .ConfigureAwait(false);

        lock (_gate)
        {
            _tasks = tasks;
            _runs = runs;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string> AddAsync(ScheduledTask task, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task.Id))
        {
            task.Id = Guid.NewGuid().ToString("N");
        }
        if (task.CreatedAt == default)
        {
            task.CreatedAt = DateTimeOffset.UtcNow;
        }

        List<ScheduledTask> snapshot;
        lock (_gate)
        {
            _tasks.Add(task);
            snapshot = _tasks.ToList();
        }

        await JsonFileStore.SaveListAsync(_tasksFilePath, snapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return task.Id;
    }

    public async Task<bool> UpdateAsync(ScheduledTask task, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task.Id))
        {
            return false;
        }

        List<ScheduledTask> snapshot;
        lock (_gate)
        {
            var index = _tasks.FindIndex(existing => existing.Id == task.Id);
            if (index < 0)
            {
                return false;
            }
            _tasks[index] = task;
            snapshot = _tasks.ToList();
        }

        await JsonFileStore.SaveListAsync(_tasksFilePath, snapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> RemoveAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return false;
        }

        List<ScheduledTask> tasksSnapshot;
        List<ScheduledTaskRun> runsSnapshot;
        lock (_gate)
        {
            var index = _tasks.FindIndex(existing => existing.Id == taskId);
            if (index < 0)
            {
                return false;
            }
            _tasks.RemoveAt(index);
            // Cascade: drop the run history for this task so
            // a deleted task doesn't leave orphans. History
            // for live tasks stays untouched.
            _runs.RemoveAll(run => run.ScheduledTaskId == taskId);
            tasksSnapshot = _tasks.ToList();
            runsSnapshot = _runs.ToList();
        }

        await JsonFileStore.SaveListAsync(_tasksFilePath, tasksSnapshot, cancellationToken)
            .ConfigureAwait(false);
        await JsonFileStore.SaveListAsync(_runsFilePath, runsSnapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> SetPausedAsync(string taskId, bool isPaused, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return false;
        }

        List<ScheduledTask> snapshot;
        lock (_gate)
        {
            var index = _tasks.FindIndex(existing => existing.Id == taskId);
            if (index < 0)
            {
                return false;
            }
            _tasks[index].IsPaused = isPaused;
            snapshot = _tasks.ToList();
        }

        await JsonFileStore.SaveListAsync(_tasksFilePath, snapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<string> RecordRunAsync(ScheduledTaskRun run, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(run.Id))
        {
            run.Id = Guid.NewGuid().ToString("N");
        }
        if (run.StartedAt == default)
        {
            run.StartedAt = DateTimeOffset.UtcNow;
        }

        List<ScheduledTaskRun> runsSnapshot;
        List<ScheduledTask> tasksSnapshot;
        lock (_gate)
        {
            _runs.Add(run);
            // Bump the parent task's LastRunAt so the
            // "上次 / 下次" column stays accurate without a
            // separate refresh. The scheduler engine
            // (follow-up slice) reads this same field.
            var taskIndex = _tasks.FindIndex(existing => existing.Id == run.ScheduledTaskId);
            if (taskIndex >= 0)
            {
                _tasks[taskIndex].LastRunAt = run.StartedAt;
            }
            runsSnapshot = _runs.ToList();
            tasksSnapshot = _tasks.ToList();
        }

        await JsonFileStore.SaveListAsync(_runsFilePath, runsSnapshot, cancellationToken)
            .ConfigureAwait(false);
        await JsonFileStore.SaveListAsync(_tasksFilePath, tasksSnapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return run.Id;
    }
}
