using AIChat.Abstractions.Configuration;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.Abstractions.Persistence;

// Persistence boundary. The app currently stores JSON locally, but the ViewModel
// only depends on this interface, so storage can be changed without touching UI.
//
// Wave 3: v0 API removed. The v0 ProjectWorkspace + Conversation model
// is gone; only WorkspaceProject + ChatSession remain. v0→v1 migration
// still runs in JsonAppRepository (dual-read window opens on the first
// v1 load, runs the MigrationCoordinator once, then operates on v1
// files only).
public interface IAppRepository
{
    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    // Ordinary settings persistence deliberately leaves credential storage alone.
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);

    // Only explicit provider/API-key changes should cross this boundary.
    Task SaveSettingsWithSecretsAsync(AppSettings settings, CancellationToken cancellationToken = default);

    // === v1 API (Wave 1.5 引入, Wave 3 后唯一) ===
    // 读取时：如果磁盘是 v0 自动 migrate；返回的形状是 v1
    Task<IReadOnlyList<WorkspaceProject>> LoadWorkspacesAsync(CancellationToken cancellationToken = default);
    Task SaveWorkspacesAsync(IReadOnlyList<WorkspaceProject> workspaces, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatSession>> LoadSessionsAsync(CancellationToken cancellationToken = default);
    Task SaveSessionsAsync(IReadOnlyList<ChatSession> sessions, CancellationToken cancellationToken = default);

    // 给 UI 显示 "data is readonly because migration incomplete" 之类的提示。
    // 返回 null = 数据可用；返回非空 = readonly + 原因。
    Task<string?> GetReadonlyReasonAsync(CancellationToken cancellationToken = default);
}
