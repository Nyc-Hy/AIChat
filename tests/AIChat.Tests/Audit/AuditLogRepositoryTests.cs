using AIChat.Domain.Audit;
using AIChat.Storage.Json;

namespace AIChat.Tests.Audit;

public sealed class AuditLogRepositoryTests : IDisposable
{
    private readonly string _tempDir;

    public AuditLogRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aichat-audit-test-{Guid.NewGuid():N}");
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
    public async Task AppendAsync_CreatesLogFileAndAppends()
    {
        var repo = new AuditLogRepository(_tempDir);

        await repo.AppendAsync(new AuditEvent
        {
            ProjectId = "proj-1",
            RunId = "run-1",
            Type = AuditEventType.ToolCallRequested,
            ToolName = "read_file",
            Summary = "Reading file"
        });

        var logPath = Path.Combine(_tempDir, "audit", "proj-1.jsonl");
        Assert.True(File.Exists(logPath));

        var content = await File.ReadAllTextAsync(logPath);
        Assert.Contains("read_file", content);
        Assert.Contains("proj-1", content);
    }

    [Fact]
    public async Task QueryAsync_ReturnsEventsForProject()
    {
        var repo = new AuditLogRepository(_tempDir);

        await repo.AppendAsync(new AuditEvent { ProjectId = "proj-1", Type = AuditEventType.ToolCallRequested, ToolName = "read_file" });
        await repo.AppendAsync(new AuditEvent { ProjectId = "proj-1", Type = AuditEventType.ToolCallApproved, ToolName = "read_file" });
        await repo.AppendAsync(new AuditEvent { ProjectId = "proj-2", Type = AuditEventType.ShellExecuted, ToolName = "run_shell" });

        var events = await repo.QueryAsync("proj-1");

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("proj-1", e.ProjectId));
    }

    [Fact]
    public async Task QueryAsync_FiltersByType()
    {
        var repo = new AuditLogRepository(_tempDir);

        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", Type = AuditEventType.ToolCallRequested });
        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", Type = AuditEventType.FileWritten });
        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", Type = AuditEventType.ToolCallRequested });

        var events = await repo.QueryAsync("p1", type: AuditEventType.ToolCallRequested);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(AuditEventType.ToolCallRequested, e.Type));
    }

    [Fact]
    public async Task QueryAsync_FiltersByTimestamp()
    {
        var repo = new AuditLogRepository(_tempDir);

        var oldEvent = new AuditEvent { ProjectId = "p1", Type = AuditEventType.AgentRunStarted, Timestamp = DateTimeOffset.Now.AddHours(-2) };
        var newEvent = new AuditEvent { ProjectId = "p1", Type = AuditEventType.AgentRunCompleted, Timestamp = DateTimeOffset.Now };
        await repo.AppendAsync(oldEvent);
        await repo.AppendAsync(newEvent);

        var events = await repo.QueryAsync("p1", after: DateTimeOffset.Now.AddMinutes(-30));

        Assert.Single(events);
        Assert.Equal(AuditEventType.AgentRunCompleted, events[0].Type);
    }

    [Fact]
    public async Task QueryAsync_FiltersByRunId()
    {
        var repo = new AuditLogRepository(_tempDir);

        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", RunId = "run-1", Type = AuditEventType.ToolCallRequested });
        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", RunId = "run-2", Type = AuditEventType.ToolCallRequested });
        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", RunId = "run-1", Type = AuditEventType.FileWritten });
        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", RunId = "", Type = AuditEventType.AgentRunStarted });

        var events = await repo.QueryAsync("p1", runId: "run-1");

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("run-1", e.RunId));
    }

    [Fact]
    public async Task QueryAsync_RunIdFilterCombinedWithTypeFilter()
    {
        var repo = new AuditLogRepository(_tempDir);

        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", RunId = "run-1", Type = AuditEventType.ToolCallRequested });
        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", RunId = "run-1", Type = AuditEventType.FileWritten });
        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", RunId = "run-1", Type = AuditEventType.ToolCallApproved });

        var events = await repo.QueryAsync("p1", runId: "run-1", type: AuditEventType.ToolCallRequested);

        Assert.Single(events);
        Assert.Equal(AuditEventType.ToolCallRequested, events[0].Type);
    }

    [Fact]
    public async Task QueryAsync_ReturnsEmptyForMissingProject()
    {
        var repo = new AuditLogRepository(_tempDir);

        var events = await repo.QueryAsync("nonexistent");

        Assert.Empty(events);
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        var repo = new AuditLogRepository(_tempDir);

        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", Type = AuditEventType.ToolCallRequested });
        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", Type = AuditEventType.FileWritten });
        await repo.AppendAsync(new AuditEvent { ProjectId = "p1", Type = AuditEventType.ShellExecuted });

        var count = await repo.CountAsync("p1");

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task QueryAsync_LimitsResults()
    {
        var repo = new AuditLogRepository(_tempDir);

        for (var i = 0; i < 10; i++)
        {
            await repo.AppendAsync(new AuditEvent { ProjectId = "p1", Type = AuditEventType.ToolCallRequested });
        }

        var events = await repo.QueryAsync("p1", maxCount: 5);

        Assert.Equal(5, events.Count);
    }

    [Fact]
    public async Task AppendAsync_RotatesWhenFileExceedsSizeLimit()
    {
        var repo = new AuditLogRepository(_tempDir, maxFileSizeBytes: 500, maxArchiveCount: 3);

        for (var i = 0; i < 20; i++)
        {
            await repo.AppendAsync(new AuditEvent
            {
                ProjectId = "p1",
                Type = AuditEventType.ToolCallRequested,
                Summary = $"Event {i} with some padding text to exceed size limit"
            });
        }

        var logDir = Path.Combine(_tempDir, "audit");
        var files = Directory.GetFiles(logDir, "p1.jsonl*");

        // Should have current file + at least one archive
        Assert.True(files.Length >= 2, $"Expected at least 2 files, got {files.Length}: {string.Join(", ", files)}");
    }

    [Fact]
    public async Task QueryAsync_ReturnsDataFromArchives()
    {
        // Use 500 bytes and 5 archives to keep enough events across files
        var repo = new AuditLogRepository(_tempDir, maxFileSizeBytes: 500, maxArchiveCount: 5);

        for (var i = 0; i < 20; i++)
        {
            await repo.AppendAsync(new AuditEvent
            {
                ProjectId = "p1",
                Type = AuditEventType.ToolCallRequested,
                Summary = $"Event number {i}"
            });
        }

        var logDir = Path.Combine(_tempDir, "audit");
        var allFiles = Directory.GetFiles(logDir, "p1*");
        Assert.True(allFiles.Length >= 2, $"Expected at least 2 files (current + archive), got {allFiles.Length}");

        // Query should return events from current + archives
        var events = await repo.QueryAsync("p1", maxCount: 100);
        Assert.True(events.Count >= 5, $"Expected >= 5 events from {allFiles.Length} files, got {events.Count}");
    }

    [Fact]
    public async Task CleanupAsync_DeletesOldArchives()
    {
        var repo = new AuditLogRepository(_tempDir, maxFileSizeBytes: 500, maxArchiveCount: 3);

        // Generate some archived data
        for (var i = 0; i < 15; i++)
        {
            await repo.AppendAsync(new AuditEvent
            {
                ProjectId = "p1",
                Type = AuditEventType.ToolCallRequested,
                Summary = $"Event {i}"
            });
        }

        var logDir = Path.Combine(_tempDir, "audit");
        var allFilesBefore = Directory.GetFiles(logDir, "p1*");

        // Cleanup archives older than now (should delete all archives, keep current)
        await repo.CleanupAsync("p1", DateTimeOffset.Now.AddSeconds(5));

        var allFilesAfter = Directory.GetFiles(logDir, "p1*");

        // Only the base file should remain
        Assert.Single(allFilesAfter);
        Assert.True(allFilesAfter[0].EndsWith("p1.jsonl"), $"Expected p1.jsonl, got {allFilesAfter[0]}");
    }
}
