using AIChat.Application.Audit;
using AIChat.Domain.Audit;
using AIChat.Storage.Json;

namespace AIChat.Tests.Audit;

public sealed class AgentRunAuditLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public AgentRunAuditLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aichat-loader-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_FiltersByRunId()
    {
        var repo = new AuditLogRepository(_tempDir);
        var now = DateTimeOffset.UtcNow;

        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.AgentRunStarted, now);
        await AppendAsync(repo, "proj-1", "run-2", AuditEventType.AgentRunStarted, now);
        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.ToolCallRequested, now.AddSeconds(1));

        var result = await AgentRunAuditLoader.LoadAsync(repo, "proj-1", "run-1", now);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal("run-1", e.RunId));
    }

    [Fact]
    public async Task LoadAsync_FiltersByProjectId()
    {
        var repo = new AuditLogRepository(_tempDir);
        var now = DateTimeOffset.UtcNow;

        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.AgentRunStarted, now);
        await AppendAsync(repo, "proj-2", "run-1", AuditEventType.AgentRunStarted, now);

        var result = await AgentRunAuditLoader.LoadAsync(repo, "proj-1", "run-1", now);

        Assert.Single(result);
        Assert.Equal("proj-1", result[0].ProjectId);
    }

    [Fact]
    public async Task LoadAsync_OrdersByTimestampAscending()
    {
        var repo = new AuditLogRepository(_tempDir);
        var now = DateTimeOffset.UtcNow;

        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.AgentRunCompleted, now.AddSeconds(10));
        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.ToolCallRequested, now.AddSeconds(2));
        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.AgentRunStarted, now);

        var result = await AgentRunAuditLoader.LoadAsync(repo, "proj-1", "run-1", now);

        Assert.Equal(3, result.Count);
        Assert.Equal(AuditEventType.AgentRunStarted, result[0].Type);
        Assert.Equal(AuditEventType.ToolCallRequested, result[1].Type);
        Assert.Equal(AuditEventType.AgentRunCompleted, result[2].Type);
    }

    [Fact]
    public async Task LoadAsync_ExcludesEventsBeforeStartedAtMinusOneMinute()
    {
        var repo = new AuditLogRepository(_tempDir);
        var now = DateTimeOffset.UtcNow;

        // This event is 2 minutes before startedAt, outside the 1-minute window
        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.AgentRunStarted, now.AddMinutes(-2));
        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.ToolCallRequested, now);

        var result = await AgentRunAuditLoader.LoadAsync(repo, "proj-1", "run-1", now);

        Assert.Single(result);
        Assert.Equal(AuditEventType.ToolCallRequested, result[0].Type);
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmptyWhenNoMatchingEvents()
    {
        var repo = new AuditLogRepository(_tempDir);
        var now = DateTimeOffset.UtcNow;

        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.AgentRunStarted, now);

        var result = await AgentRunAuditLoader.LoadAsync(repo, "proj-1", "run-nonexistent", now);

        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadAsync_MixedRunIds_OnlyTargetRunReturned()
    {
        var repo = new AuditLogRepository(_tempDir);
        var now = DateTimeOffset.UtcNow;

        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.AgentRunStarted, now);
        await AppendAsync(repo, "proj-1", "run-2", AuditEventType.AgentRunStarted, now);
        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.ToolCallRequested, now.AddSeconds(1));
        await AppendAsync(repo, "proj-1", "run-2", AuditEventType.ToolCallApproved, now.AddSeconds(2));
        await AppendAsync(repo, "proj-1", "run-1", AuditEventType.AgentRunCompleted, now.AddSeconds(3));

        var result = await AgentRunAuditLoader.LoadAsync(repo, "proj-1", "run-1", now);

        Assert.Equal(3, result.Count);
        Assert.All(result, e => Assert.Equal("run-1", e.RunId));
        Assert.Equal(AuditEventType.AgentRunStarted, result[0].Type);
        Assert.Equal(AuditEventType.ToolCallRequested, result[1].Type);
        Assert.Equal(AuditEventType.AgentRunCompleted, result[2].Type);
    }

    private static async Task AppendAsync(
        AuditLogRepository repo,
        string projectId,
        string runId,
        AuditEventType type,
        DateTimeOffset timestamp)
    {
        await repo.AppendAsync(new AuditEvent
        {
            ProjectId = projectId,
            RunId = runId,
            Type = type,
            Timestamp = timestamp
        });
    }
}
