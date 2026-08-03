using AIChat.Application.Scheduled;
using AIChat.Domain.Scheduled;

namespace AIChat.Tests.Scheduled;

// Wave 9 (parity plan §7 Wave 9): pin the registry contract
// that the ScheduledView + agent-runner wiring will depend on.
// Each test runs against a temp directory so the real
// AppRuntimeProfile files aren't touched.
public sealed class ScheduledTaskRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly string _tasksFile;
    private readonly string _runsFile;
    private readonly ScheduledTaskRegistry _registry;

    public ScheduledTaskRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aichat-sched-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _tasksFile = Path.Combine(_root, "tasks.json");
        _runsFile = Path.Combine(_root, "runs.json");
        _registry = new ScheduledTaskRegistry(_tasksFile, _runsFile);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ReloadAsync_EmptyFiles_ProducesEmptyState()
    {
        await _registry.ReloadAsync();

        Assert.Empty(_registry.Tasks);
        Assert.Empty(_registry.Runs);
    }

    [Fact]
    public async Task AddAsync_PersistsTaskAndFiresChanged()
    {
        var fired = 0;
        _registry.Changed += (_, _) => fired++;

        var task = new ScheduledTask { Name = "Daily standup", Prompt = "summarise commits" };
        var id = await _registry.AddAsync(task);

        Assert.Equal(task.Id, id);
        Assert.True(File.Exists(_tasksFile));
        var reloaded = new ScheduledTaskRegistry(_tasksFile, _runsFile);
        await reloaded.ReloadAsync();
        Assert.Single(reloaded.Tasks);
        Assert.Equal("Daily standup", reloaded.Tasks[0].Name);
        Assert.True(fired >= 1);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesRowById()
    {
        var task = new ScheduledTask { Name = "old", Prompt = "p" };
        await _registry.AddAsync(task);

        task.Name = "new";
        var updated = await _registry.UpdateAsync(task);

        Assert.True(updated);
        Assert.Equal("new", _registry.Tasks[0].Name);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsFalse()
    {
        var updated = await _registry.UpdateAsync(new ScheduledTask { Id = "missing", Name = "x" });
        Assert.False(updated);
    }

    [Fact]
    public async Task RemoveAsync_DropsTaskAndCascadesRuns()
    {
        var task = new ScheduledTask { Name = "t" };
        await _registry.AddAsync(task);
        await _registry.RecordRunAsync(new ScheduledTaskRun
        {
            ScheduledTaskId = task.Id,
            Status = ScheduledRunStatus.Completed,
        });

        var removed = await _registry.RemoveAsync(task.Id);

        Assert.True(removed);
        Assert.Empty(_registry.Tasks);
        Assert.Empty(_registry.Runs);
    }

    [Fact]
    public async Task SetPausedAsync_FlipsFlag()
    {
        var task = new ScheduledTask { Name = "t" };
        await _registry.AddAsync(task);
        Assert.False(_registry.Tasks[0].IsPaused);

        await _registry.SetPausedAsync(task.Id, true);

        Assert.True(_registry.Tasks[0].IsPaused);
    }

    [Fact]
    public async Task RecordRunAsync_AppendsRunAndBumpsLastRunAt()
    {
        var task = new ScheduledTask { Name = "t" };
        await _registry.AddAsync(task);
        Assert.Null(_registry.Tasks[0].LastRunAt);

        var run = new ScheduledTaskRun
        {
            ScheduledTaskId = task.Id,
            Status = ScheduledRunStatus.Completed,
        };
        await _registry.RecordRunAsync(run);

        Assert.Single(_registry.Runs);
        Assert.Equal(run.Id, _registry.Runs[0].Id);
        Assert.NotNull(_registry.Tasks[0].LastRunAt);
    }
}
