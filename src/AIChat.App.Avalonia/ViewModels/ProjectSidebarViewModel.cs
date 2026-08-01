using System.Collections.ObjectModel;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Projects;
using AIChat.Domain.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the left-rail "projects" surface: the list of registered projects,
// the currently selected one, and the actions to add or switch projects.
//
// PR-3 scope: pure extraction from MainWindowViewModel. Cross-VM
// coordination uses the events below. The currently-selected ProjectWorkspace
// is exposed as a public property (CurrentProject) so the rest of the app
// can read it without going through events; mutations still happen only on
// the UI thread via the commands the XAML binds to.
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

    // The source of truth for "which project is the user working on right
    // now". Other view-models read this when they need to run a task.
    public ProjectWorkspace? CurrentProject { get; private set; }

    public ProjectSidebarViewModel(IAppRepository repository, ISettingsHolder settingsHolder)
    {
        _repository = repository;
        _settingsHolder = settingsHolder;
    }

    // Replaces the project list with the supplied set and restores the
    // last-active selection. Called by the parent on startup and after
    // any project-list mutation.
    public void Refresh(IReadOnlyList<ProjectWorkspace> projects)
    {
        Projects.Clear();
        foreach (var project in projects.Where(project => !string.IsNullOrWhiteSpace(project.Path)))
        {
            Projects.Add(new ProjectCardViewModel(project.Id, project.Name, project.Path));
        }

        var target = projects.FirstOrDefault(project => project.Id == _settingsHolder.Current.LastActiveProjectId)
                     ?? projects.FirstOrDefault(project => !string.IsNullOrWhiteSpace(project.Path))
                     ?? projects.FirstOrDefault();

        ApplyProject(target);
    }

    // Switches to the project with the given id. No-op if the id does
    // not match any known project. Persists the choice so the next
    // startup can restore it.
    [RelayCommand]
    public async Task SelectProjectAsync(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        var projects = await _repository.LoadProjectsAsync();
        var project = projects.FirstOrDefault(item =>
            string.Equals(item.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (project is null || ReferenceEquals(project, CurrentProject))
        {
            return;
        }

        _settingsHolder.Current.LastActiveProjectId = project.Id;
        await _repository.SaveSettingsAsync(_settingsHolder.Current);

        // ApplyProject now fires ProjectSelected on the transition,
        // so the previous explicit Invoke here was a duplicate that
        // double-rebuilt the file tree / recomputed the context
        // budget on every user click. The early-return on
        // ReferenceEquals(project, CurrentProject) still prevents
        // a no-op click from firing it again.
        ApplyProject(project);
    }

    // Registers a new project rooted at the supplied directory. No-op
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
        var projects = (await _repository.LoadProjectsAsync()).ToList();
        var existing = projects.FirstOrDefault(project =>
            string.Equals(Path.GetFullPath(project.Path), fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new ProjectWorkspace
            {
                Name = new DirectoryInfo(fullPath).Name,
                Path = fullPath,
                VerificationCommands = new ProjectInitializer()
                    .SuggestVerificationCommands(fullPath)
                    .ToList(),
                UpdatedAt = DateTimeOffset.Now
            };
            projects.Add(existing);
            await _repository.SaveProjectsAsync(projects);
        }

        _settingsHolder.Current.LastActiveProjectId = existing.Id;
        await _repository.SaveSettingsAsync(_settingsHolder.Current);

        ApplyProject(existing);
        Refresh(projects);

        ProjectAdded?.Invoke(this, new ProjectAddedEventArgs
        {
            Project = existing,
            StatusMessage = $"已添加项目：{existing.Name}"
        });
    }

    // Removes the project with the given id from the saved list and
    // refreshes the sidebar. The currently active project (if any) is
    // swapped to the next project in the list so the rest of the UI
    // (conversation list, agent runner) keeps working. The agent's
    // local cached project reference will fall back to null on the
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

        var projects = (await _repository.LoadProjectsAsync()).ToList();
        var target = projects.FirstOrDefault(item =>
            string.Equals(item.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        projects.Remove(target);
        await _repository.SaveProjectsAsync(projects);

        // If we just removed the active project, clear the last-active
        // pointer so the next startup doesn't try to restore a project
        // that's gone.
        if (string.Equals(_settingsHolder.Current.LastActiveProjectId, projectId, StringComparison.OrdinalIgnoreCase))
        {
            _settingsHolder.Current.LastActiveProjectId = "";
            await _repository.SaveSettingsAsync(_settingsHolder.Current);
        }

        Refresh(projects);

        ProjectAdded?.Invoke(this, new ProjectAddedEventArgs
        {
            Project = null,
            StatusMessage = $"已删除项目：{target.Name}"
        });
    }

    private void ApplyProject(ProjectWorkspace? project)
    {
        var previous = CurrentProject;
        CurrentProject = project;
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
            SelectedProjectName = project.Name;
            SelectedProjectPath = string.IsNullOrWhiteSpace(project.Path) ? "未配置路径" : project.Path;
            ProjectHealth = ProjectLoadSnapshotBuilder.Build(project).HealthText;
            SelectedProjectCard = Projects.FirstOrDefault(card => card.Id == project.Id);
        }

        // Fire ProjectSelected on every transition (including the
        // initial Refresh on app startup and the null transition when
        // the last project is removed). Subscribers — FileTreeVM
        // rebuilds the file index off the new path, AgentHost
        // recomputes the context budget, etc. — need the same
        // notification for "user clicked a different project" and
        // "app launched and restored the last-active project". Pre-
        // fix, ApplyProject silently updated internal state and
        // only SelectProjectAsync (a user click handler) raised the
        // event — so a fresh app launch with a saved project in
        // projects.json never rebuilt the file tree.
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

    partial void OnSelectedProjectCardChanged(ProjectCardViewModel? value)
    {
        if (value is null || string.Equals(CurrentProject?.Id, value.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = SelectProjectAsync(value.Id);
    }
}
