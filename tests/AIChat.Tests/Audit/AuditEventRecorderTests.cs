using AIChat.Application.Audit;
using AIChat.Domain.Audit;
using AIChat.Storage.Json;

namespace AIChat.Tests.Audit;

public sealed class AuditEventRecorderTests : IDisposable
{
    private readonly string _tempDir;

    public AuditEventRecorderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aichat-recorder-test-{Guid.NewGuid():N}");
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
    public async Task RecordAsync_NullRepository_DoesNotThrow()
    {
        await AuditEventRecorder.RecordAsync(
            null, AuditEventType.ToolCallRequested, "proj-1", "run-1");
    }

    [Fact]
    public async Task RecordAsync_NormalWrite_CanBeQueriedBack()
    {
        var repo = new AuditLogRepository(_tempDir);

        await AuditEventRecorder.RecordAsync(
            repo, AuditEventType.AgentRunStarted, "proj-1", "run-1");

        var events = await repo.QueryAsync("proj-1", runId: "run-1");
        Assert.Single(events);
        Assert.Equal(AuditEventType.AgentRunStarted, events[0].Type);
        Assert.Equal("proj-1", events[0].ProjectId);
        Assert.Equal("run-1", events[0].RunId);
    }

    [Fact]
    public async Task RecordAsync_PreservesToolNameSummaryDetail()
    {
        var repo = new AuditLogRepository(_tempDir);

        await AuditEventRecorder.RecordAsync(
            repo, AuditEventType.ToolCallRequested, "proj-1", "run-1",
            toolName: "read_file", summary: "Reading src/Foo.cs", detail: "full detail here");

        var events = await repo.QueryAsync("proj-1", runId: "run-1");
        Assert.Single(events);
        Assert.Equal("read_file", events[0].ToolName);
        Assert.Equal("Reading src/Foo.cs", events[0].Summary);
        Assert.Equal("full detail here", events[0].Detail);
    }

    [Fact]
    public async Task RecordAsync_RedactsSecretsFromSummaryAndDetail()
    {
        var repo = new AuditLogRepository(_tempDir);

        await AuditEventRecorder.RecordAsync(
            repo, AuditEventType.ToolCallRequested, "proj-1", "run-1",
            summary: "Authorization: Bearer abc.def.ghi",
            detail: "{\"api_key\":\"sk-test-secret-value\"}");

        var auditEvent = Assert.Single(await repo.QueryAsync("proj-1", runId: "run-1"));
        Assert.DoesNotContain("abc.def.ghi", auditEvent.Summary);
        Assert.DoesNotContain("sk-test-secret-value", auditEvent.Detail);
        Assert.Contains("[REDACTED]", auditEvent.Summary);
        Assert.Contains("[REDACTED]", auditEvent.Detail);
    }

    [Fact]
    public async Task RecordAsync_RepositoryThrows_DoesNotPropagate()
    {
        // Use a path that will fail on write (read-only nested path on Windows)
        var repo = new AuditLogRepository("Z:\\nonexistent\\readonly\\path");

        // Should not throw even though the path is invalid
        await AuditEventRecorder.RecordAsync(
            repo, AuditEventType.ToolCallApproved, "proj-1", "run-1");
    }
}
