using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.Tests.TestDoubles;

// In-memory IAppRepository for tests. v0 + v1 API 都实现 —— 测试可自由用任何一端。
internal sealed class InMemoryAppRepository : IAppRepository
{
    private AppSettings _settings = new();
    private IReadOnlyList<WorkspaceProject> _workspaces = [];
    private IReadOnlyList<ChatSession> _sessions = [];
    private string? _readOnlyReason;

    public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_settings);

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        return Task.CompletedTask;
    }

    public Task SaveSettingsWithSecretsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => SaveSettingsAsync(settings, cancellationToken);

    public Task<IReadOnlyList<WorkspaceProject>> LoadWorkspacesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_workspaces);

    public Task SaveWorkspacesAsync(
        IReadOnlyList<WorkspaceProject> workspaces,
        CancellationToken cancellationToken = default)
    {
        _workspaces = workspaces;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ChatSession>> LoadSessionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_sessions);

    public Task SaveSessionsAsync(
        IReadOnlyList<ChatSession> sessions,
        CancellationToken cancellationToken = default)
    {
        _sessions = sessions;
        return Task.CompletedTask;
    }

    public Task<string?> GetReadonlyReasonAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_readOnlyReason);

    // Test-only: 模拟 readonly 状态
    public void SetReadonlyReason(string? reason) => _readOnlyReason = reason;
}
