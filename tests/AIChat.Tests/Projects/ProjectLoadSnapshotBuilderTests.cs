using AIChat.Application.Projects;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.Tests.Projects;

public sealed class ProjectLoadSnapshotBuilderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "AIChatProjectSnapshotTests", Guid.NewGuid().ToString("N"));

    public ProjectLoadSnapshotBuilderTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Build_ReportsMissingPath()
    {
        var snapshot = ProjectLoadSnapshotBuilder.Build(new WorkspaceProject { Folders = [new WorkspaceFolder { Id = "f1", Path = "" }], PrimaryFolderId = "f1"}, []);

        Assert.Contains("未设置项目路径", snapshot.HealthText);
        Assert.Contains("设置项目路径", snapshot.RecommendationText);
    }

    [Fact]
    public void Build_DetectsProjectProfileAndHealth()
    {
        File.WriteAllText(Path.Combine(_tempDir, "AGENTS.md"), "# Agent");
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "# Readme");
        File.WriteAllText(Path.Combine(_tempDir, "App.sln"), "");
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));
        var workspace = new WorkspaceProject {
            Folders = [new WorkspaceFolder { Id = "f1", Path = _tempDir }], PrimaryFolderId = "f1",
            VerificationCommands =
            [
                new ProjectVerificationCommand { Name = "test", Command = "dotnet test" }
            ]
        };

        var snapshot = ProjectLoadSnapshotBuilder.Build(workspace, []);

        Assert.Contains("路径可用", snapshot.HealthText);
        Assert.Contains("AGENTS.md 已就绪", snapshot.HealthText);
        Assert.Contains(".NET", snapshot.ProfileText);
        Assert.Contains("src", snapshot.ProfileText);
        Assert.Contains("README.md", snapshot.ProfileText);
    }

    [Fact]
    public void Build_ReportsAgentRunActivity()
    {
        var session = new Project {
            AgentRuns =
            [
                new AgentRun
                {
                    Goal = "fix login",
                    Status = AgentRunStatus.Completed,
                    AcceptanceStatus = AgentRunAcceptanceStatus.NeedsChanges,
                    StartedAt = DateTimeOffset.Now
                }
            ]
        };
        var workspace = new WorkspaceProject {
            Folders = [new WorkspaceFolder { Id = "f1", Path = _tempDir }], PrimaryFolderId = "f1"
        };

        var snapshot = ProjectLoadSnapshotBuilder.Build(workspace, [session]);

        Assert.Contains("1 个对话", snapshot.ActivityText);
        Assert.Contains("1 次运行", snapshot.ActivityText);
        Assert.Contains("1 个需修改", snapshot.ActivityText);
        Assert.Contains("最近：完成", snapshot.ActivityText);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
