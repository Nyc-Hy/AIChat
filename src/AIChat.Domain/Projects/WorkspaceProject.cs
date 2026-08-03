namespace AIChat.Domain.Projects;

// Wave 1: 顶替 ProjectWorkspace（plan §2.2 + 修正 #3 #8）。
// 1 个 project = N 个 folder roots + 1 个 primary；跟 Codex 实际行为对齐。
//
// 修正 #3：删了原设计的 WorkspaceFolder.IsPrimary 字段 —— 反规范化会跟
// WorkspaceProject.PrimaryFolderId 漂移。现在 primary 只由 PrimaryFolderId
// 决定，Folder 本身不知道自己是不是 primary（避免双源真值）。
//
// 修正 #8：PrimaryPath getter 找不到 PrimaryFolderId 对应 folder 时**抛异常**
// 而不是静默回退到 Folders[0]。loud failure 让 UI 写入路径必须保证一致性。
//
// 旧 ProjectWorkspace 保留为 [Obsolete]，UI 切换前仍能编译通过；Wave 2
// 完成后删掉旧类型。
public sealed class WorkspaceProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "AIChat";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public List<WorkspaceFolder> Folders { get; set; } = [];
    public string PrimaryFolderId { get; set; } = "";

    public List<Context.PinnedContextItem> PinnedContext { get; set; } = [];
    public List<Artifacts.InputArtifact> InputArtifacts { get; set; } = [];
    public List<Memory.MemoryEntry> Memories { get; set; } = [];
    public List<Memory.MemoryEntry> PendingMemories { get; set; } = [];
    public List<ProjectVerificationCommand> VerificationCommands { get; set; } = [];

    // Per-project tool permission overrides (tool ID -> mode name).
    // Merged with global AppSettings.ToolPermissionModes; project values take precedence.
    public Dictionary<string, string> ProjectToolPermissionModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // 读取时返回 primary folder 的路径。PrimaryFolderId 找不到对应 folder 时
    // 抛 InvalidOperationException（loud failure；不静默回退）。
    // 调用方应在写入时保证一致性：要么 PrimaryFolderId == Folders[0].Id
    // （常见情况），要么明确指定某个 folder id。
    public string PrimaryPath
    {
        get
        {
            if (Folders.Count == 0)
            {
                throw new InvalidOperationException(
                    $"WorkspaceProject '{Id}' ({Name}) has no folders but PrimaryFolderId='{PrimaryFolderId}'. " +
                    "This is an inconsistent state — either add a folder or set PrimaryFolderId to empty.");
            }

            var primary = Folders.FirstOrDefault(folder => folder.Id == PrimaryFolderId);
            if (primary is null)
            {
                throw new InvalidOperationException(
                    $"WorkspaceProject '{Id}' ({Name}) PrimaryFolderId='{PrimaryFolderId}' does not match any folder. " +
                    $"Available folder ids: {string.Join(", ", Folders.Select(f => f.Id))}");
            }

            return primary.Path;
        }
    }

    // Convenience for the many call-sites that want to read a path-or-null
    // rather than catch InvalidOperationException. Returns null when:
    //   - Folders is empty (newly-created project not yet rooted)
    //   - PrimaryFolderId doesn't match any folder (inconsistent state)
    // Mirrors the old Wave 2 WorkspaceProjectExtensions.GetPath() shim
    // (deleted in Wave 2.11) so callers can stop catching the exception.
    public string? TryGetPrimaryPath()
    {
        try
        {
            return PrimaryPath;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

public sealed class WorkspaceFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = "";
    public string? DisplayName { get; set; }
    // 修正 #3：删 IsPrimary 字段。primary 状态由 WorkspaceProject.PrimaryFolderId 决定。
}
