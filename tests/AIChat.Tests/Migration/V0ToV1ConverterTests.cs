using AIChat.Domain.Chat;
using AIChat.Domain.Context;
using AIChat.Domain.Memory;
using AIChat.Domain.Projects;
using AIChat.Storage.Json.Migration;

// v0 types (ProjectWorkspace, Conversation) are [Obsolete] since Wave 3.
// The migration tests are the one place that still construct them, so
// we suppress CS0618 at the file level.
#pragma warning disable CS0618

namespace AIChat.Tests.Migration;

// T-MIG-VC layer: v0 → v1 in-memory 转换的单元测试。
// 覆盖 plan §7.1 (V0ToV1ConverterTests 8 个) + 修正 #3 / 修正 #4 / 修正 #5:
//   修正 #3: WorkspaceProject 不再含 IsPrimary
//   修正 #4: empty-path 项目保留 metadata
//   修正 #5: conversations 总是迁移(即使 path 缺失)
public sealed class V0ToV1ConverterTests
{
    [Fact]
    public void Convert_EmptyList_ReturnsEmptyResult()
    {
        var result = V0ToV1Converter.Convert([]);

        Assert.Empty(result.WorkspaceProjects);
        Assert.Empty(result.Sessions);
    }

    [Fact]
    public void Convert_SingleProjectWithPath_CreatesOneFolder()
    {
        var v0 = new ProjectWorkspace
        {
            Id = "p1",
            Name = "AIChat",
            Path = "/tmp/repo",
        };

        var result = V0ToV1Converter.Convert([v0]);

        var workspace = Assert.Single(result.WorkspaceProjects);
        Assert.Equal("p1", workspace.Id);
        Assert.Equal("AIChat", workspace.Name);
        var folder = Assert.Single(workspace.Folders);
        Assert.Equal("/tmp/repo", folder.Path);
        Assert.Equal(workspace.PrimaryFolderId, folder.Id);
        // 修正 #3: 不再有 IsPrimary 字段
        Assert.Equal("/tmp/repo", workspace.PrimaryPath);
    }

    [Fact]
    public void Convert_SingleProjectWithEmptyPath_PreservesMetadataAndOmitsFolder()
    {
        // 修正 #4: 空 path 项目保留 PinnedContext / Memories / VerificationCommands
        // 但不创建 folder(没根目录)
        var v0 = new ProjectWorkspace
        {
            Id = "p1",
            Name = "AIChat",
            Path = "",
            PinnedContext = [new PinnedContextItem { Id = "ctx1", Path = "AGENTS.md", Note = "spec" }],
            Memories = [new MemoryEntry { Id = "m1", Content = "remember this" }],
        };

        var result = V0ToV1Converter.Convert([v0]);

        var workspace = Assert.Single(result.WorkspaceProjects);
        Assert.Empty(workspace.Folders);
        Assert.Equal("", workspace.PrimaryFolderId);
        // PrimaryPath 抛 InvalidOperationException 是预期行为
        Assert.Throws<InvalidOperationException>(() => workspace.PrimaryPath);
        // Metadata preserved
        Assert.Single(workspace.PinnedContext);
        Assert.Equal("AGENTS.md", workspace.PinnedContext[0].Path);
        Assert.Single(workspace.Memories);
    }

    [Fact]
    public void Convert_ProjectWithConversations_MigratesEachToProjectSession()
    {
        // 修正 #5: 无论 path 是否为空,conversations 都要迁移
        var v0 = new ProjectWorkspace
        {
            Id = "p1",
            Path = "/tmp/repo",
            Conversations =
            [
                new Conversation { Id = "c1", ProjectId = "p1", Title = "first" },
                new Conversation { Id = "c2", ProjectId = "p1", Title = "second" },
            ],
        };

        var result = V0ToV1Converter.Convert([v0]);

        Assert.Equal(2, result.Sessions.Count);
        Assert.All(result.Sessions, s => Assert.IsType<Project>(s));
        Assert.All(result.Sessions.OfType<Project>(),
            p => Assert.Equal("p1", p.WorkspaceId));
        // ConversationId → SessionId 保留
        Assert.Equal(new[] { "c1", "c2" },
            result.Sessions.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Convert_PreservesWorkspaceIdNameAndUpdatedAt()
    {
        var updatedAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var v0 = new ProjectWorkspace
        {
            Id = "p1",
            Name = "AIChat",
            Path = "/tmp/repo",
            UpdatedAt = updatedAt,
        };

        var result = V0ToV1Converter.Convert([v0]);
        var workspace = result.WorkspaceProjects[0];

        Assert.Equal("p1", workspace.Id);
        Assert.Equal("AIChat", workspace.Name);
        Assert.Equal(updatedAt, workspace.UpdatedAt);
    }

    [Fact]
    public void Convert_PreservesPinnedContextMemoriesAndVerificationCommands()
    {
        var v0 = new ProjectWorkspace
        {
            Id = "p1",
            Path = "/tmp/repo",
            PinnedContext = [new PinnedContextItem { Id = "ctx", Path = "AGENTS.md" }],
            Memories = [new MemoryEntry { Id = "m", Content = "x" }],
            PendingMemories = [new MemoryEntry { Id = "pm", Content = "y" }],
            VerificationCommands =
            [
                new ProjectVerificationCommand { Command = "dotnet build", Name = "build" }
            ],
            ProjectToolPermissionModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bash"] = "full",
            },
        };

        var result = V0ToV1Converter.Convert([v0]);
        var workspace = result.WorkspaceProjects[0];

        Assert.Single(workspace.PinnedContext);
        Assert.Equal("AGENTS.md", workspace.PinnedContext[0].Path);
        Assert.Single(workspace.Memories);
        Assert.Single(workspace.PendingMemories);
        Assert.Single(workspace.VerificationCommands);
        Assert.Equal("dotnet build", workspace.VerificationCommands[0].Command);
        Assert.Equal("build", workspace.VerificationCommands[0].Name);
        Assert.Single(workspace.ProjectToolPermissionModes);
        Assert.Equal("full", workspace.ProjectToolPermissionModes["bash"]);
    }

    [Fact]
    public void Convert_PreservesChatSessionMessagesCallDetailsAndAgentRuns()
    {
        // 验证 conversations 的 messages / call details / agent runs
        // 被原样搬到 v1 ChatSession(单条端到端保真度测试)
        var v0 = new ProjectWorkspace
        {
            Id = "p1",
            Path = "/tmp/repo",
            Conversations =
            [
                new Conversation
                {
                    Id = "c1",
                    ProjectId = "p1",
                    Title = "test",
                    Messages =
                    [
                        new ChatMessage { Id = "m1", Role = ChatRole.User, Content = "hi" }
                    ],
                    CallDetails =
                    [
                        new LlmCallDetail { Id = "call1", Model = "claude-sonnet-4.5" }
                    ],
                    AgentRuns =
                    [
                        new AgentRun { Id = "run1", Goal = "build feature" }
                    ],
                }
            ],
        };

        var result = V0ToV1Converter.Convert([v0]);
        var session = result.Sessions[0];

        Assert.Single(session.Messages);
        Assert.Equal("m1", session.Messages[0].Id);
        Assert.Equal(ChatRole.User, session.Messages[0].Role);
        Assert.Equal("hi", session.Messages[0].Content);
        Assert.Single(session.CallDetails);
        Assert.Equal("claude-sonnet-4.5", session.CallDetails[0].Model);
        Assert.Single(session.AgentRuns);
        Assert.Equal("build feature", session.AgentRuns[0].Goal);
    }

    [Fact]
    public void Convert_MultipleProjects_EachGetsOwnFolderAndSessions()
    {
        var v0 = new List<ProjectWorkspace>
        {
            new() { Id = "p1", Name = "A", Path = "/tmp/a", Conversations = [new Conversation { Id = "c1", ProjectId = "p1" }] },
            new() { Id = "p2", Name = "B", Path = "/tmp/b", Conversations = [new Conversation { Id = "c2", ProjectId = "p2" }, new Conversation { Id = "c3", ProjectId = "p2" }] },
        };

        var result = V0ToV1Converter.Convert(v0);

        Assert.Equal(2, result.WorkspaceProjects.Count);
        Assert.Equal(3, result.Sessions.Count);
        // Session WorkspaceId 跟原项目对得上
        var sessionsByProject = result.Sessions.OfType<Project>().GroupBy(s => s.WorkspaceId).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(1, sessionsByProject["p1"]);
        Assert.Equal(2, sessionsByProject["p2"]);
    }
}
