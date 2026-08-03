using System.Collections.ObjectModel;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Projects;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the left-rail "projects" surface: the list of registered workspaces,
// the currently selected one, and the actions to add or switch workspaces.
//
// PR-3 scope: pure extraction from MainWindowViewModel. Cross-VM
// coordination uses the events below. The currently-selected WorkspaceProject
// is exposed as a public property (CurrentProject) so the rest of the app
// can read it without going through events; mutations still happen only on
// the UI thread via the commands the XAML binds to.
//
// Wave 2: switched from ProjectWorkspace (v0) to WorkspaceProject (v1).
// - project.Path → project.PrimaryPath
// - LoadProjectsAsync / SaveProjectsAsync → LoadWorkspacesAsync / SaveWorkspacesAsync
// - New workspaces are created with a single Folder + matching PrimaryFolderId
public sealed partial class ProjectSidebarViewModel : ViewModelBase
{
    private readonly IAppRepository _repository;
    private readonly ISettingsHolder _settingsHolder;

    public event EventHandler<ProjectSelectionChangedEventArgs>? ProjectSelected;
    public event EventHandler<ProjectAddedEventArgs>? ProjectAdded;

    [ObservableProperty]
    private ProjectCardViewModel? selectedProjectCard;

    [ObservableProperty]
    private string selectedProjectName = "未选择项目";

    [ObservableProperty]
    private string selectedProjectPath = "";

    [ObservableProperty]
    private string projectHealth = "添加或初始化项目后开始。";

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    // The source of truth for "which workspace is the user working on right
    // now". Other view-models read this when they need to run a task.
    public WorkspaceProject? CurrentProject { get; private set; }

    // 当前项目绑定的会话。Wave 2 之前嵌在 ProjectWorkspace.Conversations 里;
    // 现在 sessions 是 v1 模型外部存储,sidebar 在 ApplyProject 时从 repo 拉一次。
    // List<Project> 子类型(WorkspaceId == CurrentProject.Id);Standalone 不算。
    public IReadOnlyList<ChatSession> CurrentProjectSessions { get; private set; } = [];

    public ProjectSidebarViewModel(IAppRepository repository, ISettingsHolder settingsHolder)
    {
        _repository = repository;
        _settingsHolder = settingsHolder;
    }

    // Replaces the workspace list with the supplied set and restores the
    // last-active selection. Called by the parent on startup and after
    // any workspace-list mutation.
    public void Refresh(IReadOnlyList<WorkspaceProject> workspaces)
    {
        Projects.Clear();
        foreach (var workspace in workspaces.Where(workspace => !string.IsNullOrWhiteSpace(SafePrimaryPath(workspace))))
        {
            var primaryPath = SafePrimaryPath(workspace) ?? "";
            var card = new ProjectCardViewModel(workspace.Id, workspace.Name, primaryPath);
            card.SyncFolders(workspace);
            Projects.Add(card);
        }

        var target = workspaces.FirstOrDefault(workspace => workspace.Id == _settingsHolder.Current.LastActiveProjectId)
                     ?? workspaces.FirstOrDefault(workspace => !string.IsNullOrWhiteSpace(SafePrimaryPath(workspace)))
                     ?? workspaces.FirstOrDefault();

        ApplyProject(target);
    }

    // Switches to the workspace with the given id. No-op if the id does
    // not match any known workspace. Persists the choice so the next
    // startup can restore it.
    [RelayCommand]
    public async Task SelectProjectAsync(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        var workspaces = await _repository.LoadWorkspacesAsync();
        var workspace = workspaces.FirstOrDefault(item =>
            string.Equals(item.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (workspace is null || ReferenceEquals(workspace, CurrentProject))
        {
            return;
        }

        _settingsHolder.Current.LastActiveProjectId = workspace.Id;
        await _repository.SaveSettingsAsync(_settingsHolder.Current);

        // ApplyProject now fires ProjectSelected on the transition,
        // so the previous explicit Invoke here was a duplicate that
        // double-recomputed project-dependent context
        // budget on every user click. The early-return on
        // ReferenceEquals(workspace, CurrentProject) still prevents
        // a no-op click from firing it again.
        ApplyProject(workspace);
    }

    // Registers a new workspace rooted at the supplied directory. No-op
    // (but still raises a ProjectAdded event with the failure message)
    // if the path is missing or already registered.
    [RelayCommand]
    public async Task AddProjectAsync(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            ProjectAdded?.Invoke(this, new ProjectAddedEventArgs
            {
                Project = null,
                StatusMessage = "请选择存在的项目目录。"
            });
            return;
        }

        var fullPath = Path.GetFullPath(projectPath);
        var workspaces = (await _repository.LoadWorkspacesAsync()).ToList();
        // Wave 3 (plan §3.2): a workspace can now have multiple folders.
        // If the picked path matches an existing folder in some
        // workspace, treat that as "already added" — no duplicate
        // workspace is created. Otherwise create a new workspace
        // with one folder + that folder as primary.
        var match = workspaces.FirstOrDefault(workspace =>
            workspace.Folders.Any(folder =>
                string.Equals(folder.Path, fullPath, StringComparison.OrdinalIgnoreCase)));
        WorkspaceProject existing = match!;
        if (existing is null)
        {
            var folderId = Guid.NewGuid().ToString("N");
            existing = new WorkspaceProject
            {
                Name = new DirectoryInfo(fullPath).Name,
                Folders = [new WorkspaceFolder { Id = folderId, Path = fullPath }],
                PrimaryFolderId = folderId,
                VerificationCommands = new ProjectInitializer()
                    .SuggestVerificationCommands(fullPath)
                    .ToList(),
                UpdatedAt = DateTimeOffset.Now
            };
            workspaces.Add(existing);
            await _repository.SaveWorkspacesAsync(workspaces);
        }

        _settingsHolder.Current.LastActiveProjectId = existing.Id;
        await _repository.SaveSettingsAsync(_settingsHolder.Current);

        ApplyProject(existing);
        Refresh(workspaces);

        ProjectAdded?.Invoke(this, new ProjectAddedEventArgs
        {
            Project = existing,
            StatusMessage = $"已添加项目：{existing.Name}"
        });
    }

    // Wave 3: add an extra folder to the currently selected project.
    // The new folder is appended to Folders (PrimaryFolderId stays on
    // whatever it was — the user can switch via SetPrimaryFolderAsync).
    // If the path is already in Folders, the call is a no-op.
    [RelayCommand]
    public async Task AddFolderToCurrentProjectAsync(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            ProjectAdded?.Invoke(this, new ProjectAddedEventArgs
            {
                Project = CurrentProject,
                StatusMessage = "请选择存在的目录。"
            });
            return;
        }
        var fullPath = Path.GetFullPath(folderPath);
        var workspace = CurrentProject;
        if (workspace is null)
        {
            ProjectAdded?.Invoke(this, new ProjectAddedEventArgs
            {
                Project = null,
                StatusMessage = "请先选择项目。"
            });
            return;
        }
        if (workspace.Folders.Any(folder =>
            string.Equals(folder.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            ProjectAdded?.Invoke(this, new ProjectAddedEventArgs
            {
                Project = workspace,
                StatusMessage = "该目录已经在项目里。"
            });
            return;
        }
        var folderId = Guid.NewGuid().ToString("N");
        workspace.Folders.Add(new WorkspaceFolder { Id = folderId, Path = fullPath });
        workspace.UpdatedAt = DateTimeOffset.Now;
        await _repository.SaveWorkspacesAsync(new[] { workspace }
            .Concat((await _repository.LoadWorkspacesAsync()).Where(w => w.Id != workspace.Id))
            .ToList());
        var card = Projects.FirstOrDefault(c => c.Id == workspace.Id);
        card?.SyncFolders(workspace);
        ProjectAdded?.Invoke(this, new ProjectAddedEventArgs
        {
            Project = workspace,
            StatusMessage = $"已添加目录：{folderPath}"
        });
    }

    // Wave 3: set the primary folder on the currently selected project.
    // PrimaryFolderId is the single source of truth — changing it
    // updates WorkspaceProject.PrimaryPath and the sidebar card
    // (SyncFolders re-derives the badge + Path).
    [RelayCommand]
    public async Task SetPrimaryFolderAsync(string? folderId)
    {
        if (string.IsNullOrWhiteSpace(folderId)) return;
        var workspace = CurrentProject;
        if (workspace is null) return;
        if (!workspace.Folders.Any(f => f.Id == folderId)) return;
        workspace.PrimaryFolderId = folderId;
        workspace.UpdatedAt = DateTimeOffset.Now;
        await _repository.SaveWorkspacesAsync(new[] { workspace }
            .Concat((await _repository.LoadWorkspacesAsync()).Where(w => w.Id != workspace.Id))
            .ToList());
        var card = Projects.FirstOrDefault(c => c.Id == workspace.Id);
        if (card is not null)
        {
            card.SyncFolders(workspace);
            // Path 字段(单一路径,给 backward-compatible callers 用)也要同步
            try { card.Path = workspace.PrimaryPath; } catch { card.Path = ""; }
        }
        ApplyProject(workspace);
        ProjectAdded?.Invoke(this, new ProjectAddedEventArgs
        {
            Project = workspace,
            StatusMessage = "已切换主目录。"
        });
    }

    // Removes the workspace with the given id from the saved list and
    // refreshes the sidebar. The currently active workspace (if any) is
    // swapped to the next workspace in the list so the rest of the UI
    // (conversation list, agent runner) keeps working. The agent's
    // local cached workspace reference will fall back to null on the
    // next refresh — the next SendTaskCommand will block on the
    // "需要项目" guard until the user picks a new one.
    //
    // The on-disk JSON under <AppData>/AIChat/projects.json is the
    // source of truth; everything else (sidebar, conversation list)
    // re-reads from the repo on Refresh.
    [RelayCommand]
    public async Task RemoveProjectAsync(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        var workspaces = (await _repository.LoadWorkspacesAsync()).ToList();
        var target = workspaces.FirstOrDefault(item =>
            string.Equals(item.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        workspaces.Remove(target);
        await _repository.SaveWorkspacesAsync(workspaces);

        // If we just removed the active workspace, clear the last-active
        // pointer so the next startup doesn't try to restore a workspace
        // that's gone.
        if (string.Equals(_settingsHolder.Current.LastActiveProjectId, projectId, StringComparison.OrdinalIgnoreCase))
        {
            _settingsHolder.Current.LastActiveProjectId = "";
            await _repository.SaveSettingsAsync(_settingsHolder.Current);
        }

        Refresh(workspaces);

        ProjectAdded?.Invoke(this, new ProjectAddedEventArgs
        {
            Project = null,
            StatusMessage = $"已删除项目：{target.Name}"
        });
    }

    private void ApplyProject(WorkspaceProject? project)
    {
        var previous = CurrentProject;
        CurrentProject = project;
        // Sessions 加载是 async 的;ApplyProject 是 sync 的(handler 都是
        // Command 路径,不会从 UI 同步上下文 fire)。先清空让 UI 立刻进入
        // "未选"状态,MainWindowViewModel 在 ProjectSelected handler 里
        // 调 ReloadCurrentProjectSessionsAsync 异步填充。
        CurrentProjectSessions = [];
        foreach (var card in Projects)
        {
            card.IsSelected = card.Id == project?.Id;
        }
        if (project is null)
        {
            SelectedProjectCard = null;
            SelectedProjectName = "未选择项目";
            SelectedProjectPath = "";
            ProjectHealth = "添加或初始化项目后开始。";
        }
        else
        {
            var primaryPath = SafePrimaryPath(project) ?? "未配置路径";
            SelectedProjectName = project.Name;
            SelectedProjectPath = primaryPath;
            // Wave 2: sidebar health 走 path/file 检查(sessions 数据是
            // agent 跑的统计,sidebar 不关心),所以 sessions 暂时是空。
            // ProjectSelected 之后 MainWindowViewModel 会 await sessions
            // 加载并 refresh 其它 VM,sidebar 自己保持只读 health。
            ProjectHealth = ProjectLoadSnapshotBuilder.Build(project, []).HealthText;
            SelectedProjectCard = Projects.FirstOrDefault(card => card.Id == project.Id);
        }

        // Fire ProjectSelected on every transition (including the
        // initial Refresh on app startup and the null transition when
        // the last workspace is removed). Subscribers such as AgentHost
        // recompute the context budget off the new path and need the same
        // notification for "user clicked a different workspace" and
        // "app launched and restored the last-active workspace".
        if (!ReferenceEquals(previous, project))
        {
            ProjectSelected?.Invoke(this, new ProjectSelectionChangedEventArgs
            {
                Project = project,
                StatusMessage = project is null
                    ? "未选择项目。"
                    : $"已切换到项目：{project.Name}"
            });
        }
    }

    // Public: MainWindowViewModel 在 ProjectSelected handler 里调,
    // Public: AgentRunnerViewModel 在写完新 session 后调,更新 in-memory
    // session 列表(不重新从磁盘 load)。fire SessionsReloaded 让 UI 跟着刷新。
    public void UpdateCurrentProjectSessions(IReadOnlyList<ChatSession> sessions)
    {
        CurrentProjectSessions = sessions;
        SessionsReloaded?.Invoke(this, EventArgs.Empty);
    }

    // Public: MainWindowViewModel 在 ProjectSelected handler 里调,
    // reload 当前项目的 sessions。会 fire SessionsReloaded 事件让
    // conversation list / run history 跟着 refresh。
    public async Task ReloadCurrentProjectSessionsAsync()
    {
        if (CurrentProject is null)
        {
            CurrentProjectSessions = [];
            SessionsReloaded?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            var all = await _repository.LoadSessionsAsync();
            CurrentProjectSessions = all
                .OfType<Project>()
                .Where(session => string.Equals(session.WorkspaceId, CurrentProject.Id, StringComparison.OrdinalIgnoreCase))
                .Cast<ChatSession>()
                .ToList();
        }
        catch
        {
            // repo 出错(磁盘/权限/migration 失败)→ 退到空 list,UI 仍能跑
            CurrentProjectSessions = [];
        }

        SessionsReloaded?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? SessionsReloaded;

    // 项目路径有"配置"和"未配置"两种状态。PrimaryPath 在 PrimaryFolderId 跟
    // Folders 漂移时会抛 InvalidOperationException — sidebar 这一层只关心
    // 路径能不能用，所以包一层 safe 读。
    private static string? SafePrimaryPath(WorkspaceProject workspace)
    {
        try
        {
            return workspace.PrimaryPath;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    partial void OnSelectedProjectCardChanged(ProjectCardViewModel? value)
    {
        if (value is null || string.Equals(CurrentProject?.Id, value.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = SelectProjectAsync(value.Id);
    }
}
