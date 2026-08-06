using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

// v0 types (ProjectWorkspace, Conversation) are [Obsolete] since Wave 3.
// The v0→v1 migration path is the one place that must still consume them;
// we suppress CS0618 at the file level so the build stays clean while
// the type itself documents the "kept only for migration" intent.
#pragma warning disable CS0618

namespace AIChat.Storage.Json.Migration;

// Wave 1: 把 v0 schema 转换成 v1 schema（plan §2 / §3）。
// 纯 in-memory 转换：不读不写盘，只接受 v0 输入返回 v1 输出。
// 写盘 / 备份 / dual-read 在 MigrationCoordinator 里。
public static class V0ToV1Converter
{
    public sealed record Converted(
        IReadOnlyList<WorkspaceProject> WorkspaceProjects,
        IReadOnlyList<ChatSession> Sessions);

    public static Converted Convert(IReadOnlyList<ProjectWorkspace> v0Projects)
    {
        ArgumentNullException.ThrowIfNull(v0Projects);

        var workspaceProjects = new List<WorkspaceProject>(v0Projects.Count);
        var sessions = new List<ChatSession>();

        foreach (var v0 in v0Projects)
        {
            var (workspace, projectSessions) = ConvertProject(v0);
            workspaceProjects.Add(workspace);
            sessions.AddRange(projectSessions);
        }

        return new Converted(workspaceProjects, sessions);
    }

    private static (WorkspaceProject, IReadOnlyList<ChatSession>) ConvertProject(ProjectWorkspace v0)
    {
        WorkspaceProject workspace;
        if (string.IsNullOrWhiteSpace(v0.Path))
        {
            // 空 path 项目保留 metadata（PinnedContext / Memories 等），但不放 folder
            // 跟 v0 LoadProjectsAsync 行为对齐：UI 看到空 path 项目没根目录
            workspace = new WorkspaceProject
            {
                Id = v0.Id,
                Name = v0.Name,
                UpdatedAt = v0.UpdatedAt,
                PinnedContext = v0.PinnedContext,
                InputArtifacts = v0.InputArtifacts,
                Memories = v0.Memories,
                PendingMemories = v0.PendingMemories,
                VerificationCommands = v0.VerificationCommands,
                ProjectToolPermissionModes = v0.ProjectToolPermissionModes,
            };
        }
        else
        {
            var folderId = Guid.NewGuid().ToString("N");
            var folder = new WorkspaceFolder
            {
                Id = folderId,
                Path = v0.Path.Trim(),
                DisplayName = null,
            };
            workspace = new WorkspaceProject
            {
                Id = v0.Id,
                Name = v0.Name,
                UpdatedAt = v0.UpdatedAt,
                Folders = [folder],
                PrimaryFolderId = folderId,
                PinnedContext = v0.PinnedContext,
                InputArtifacts = v0.InputArtifacts,
                Memories = v0.Memories,
                PendingMemories = v0.PendingMemories,
                VerificationCommands = v0.VerificationCommands,
                ProjectToolPermissionModes = v0.ProjectToolPermissionModes,
            };
        }

        // Conversations → ChatSession.Project（无论 path 是否为空都迁移，
        // 因为旧数据可能 path 缺失但 conversations 还有效）
        var chatSessions = new List<ChatSession>(v0.Conversations.Count);
        foreach (var conv in v0.Conversations)
        {
            chatSessions.Add(new Project
            {
                WorkspaceId = v0.Id,
                Id = conv.Id,
                Title = conv.Title,
                UpdatedAt = conv.UpdatedAt,
                Messages = conv.Messages,
                CallDetails = conv.CallDetails,
                AgentRuns = conv.AgentRuns,
            });
        }

        return (workspace, chatSessions);
    }
}
