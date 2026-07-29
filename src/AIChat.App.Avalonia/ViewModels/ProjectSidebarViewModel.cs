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

        ApplyProject(project);
        ProjectSelected?.Invoke(this, new ProjectSelectionChangedEventArgs
        {
            Project = project,
            StatusMessage = $"已切换到项目：{project.Name}"
        });
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

    private void ApplyProject(ProjectWorkspace? project)
    {
        CurrentProject = project;
        if (project is null)
        {
            SelectedProjectCard = null;
            SelectedProjectName = "未选择项目";
            SelectedProjectPath = "";
            ProjectHealth = "添加或初始化项目后开始。";
            return;
        }

        SelectedProjectName = project.Name;
        SelectedProjectPath = string.IsNullOrWhiteSpace(project.Path) ? "未配置路径" : project.Path;
        ProjectHealth = ProjectLoadSnapshotBuilder.Build(project).HealthText;
        SelectedProjectCard = Projects.FirstOrDefault(card => card.Id == project.Id);
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
