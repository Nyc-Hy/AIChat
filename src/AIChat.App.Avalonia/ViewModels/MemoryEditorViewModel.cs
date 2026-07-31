using System.Collections.ObjectModel;
using AIChat.Abstractions.Persistence;
using AIChat.Application.Memory;
using AIChat.Domain.Memory;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Manages the user's view of the current project's memory entries:
// add a new entry (with category), delete an existing entry. Mirrors
// the JSON persistence pattern AgentRunnerViewModel uses: load all
// projects, find the current one, write back the full list. The
// MemoryService.TryCreate method provides the secret-detection and
// category rules so the user-facing validation is centralised in
// the application layer.
//
// The host opens this view via ⌘⇧M and the IsMemoryEditorOpen flag
// on MainWindowViewModel. /memory (the slash command) stays as a
// quick read-only summary in the activity feed; this view is the
// editor.
public sealed partial class MemoryEditorViewModel : ViewModelBase
{
    private readonly IAppRepository _repository;
    private readonly ProjectSidebarViewModel _sidebar;

    // Flat list of entries shown in the modal. Newest first so the
    // user sees what was added most recently without scrolling. The
    // category is rendered on each row (small badge) so the grouping
    // is implicit — no need to slice into 4 sub-collections in XAML.
    public ObservableCollection<MemoryEntryViewModel> Entries { get; } = [];

    public IReadOnlyList<MemoryCategory> AvailableCategories { get; } =
        Enum.GetValues<MemoryCategory>();

    [ObservableProperty]
    private string newContent = "";

    [ObservableProperty]
    private MemoryCategory newCategory = MemoryCategory.Project;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string? errorMessage;

    // The "无项目" message shows when the user opens the editor
    // without an active project. Save / Add stay disabled (CanAdd
    // returns false) so the user can't accidentally write memory to
    // a project that isn't loaded.
    public bool IsAvailable => _sidebar.CurrentProject is not null;
    public string ProjectName => _sidebar.CurrentProject?.Name ?? "";

    public int EntryCount => Entries.Count;

    public MemoryEditorViewModel(IAppRepository repository, ProjectSidebarViewModel sidebar)
    {
        _repository = repository;
        _sidebar = sidebar;

        // The active project can change while the editor is closed
        // (sidebar click) and while it's open. Either way the list
        // needs to refresh — fire whenever the project reference
        // flips.
        _sidebar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProjectSidebarViewModel.CurrentProject)
                                or nameof(ProjectSidebarViewModel.SelectedProjectName))
            {
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(ProjectName));
                AddCommand.NotifyCanExecuteChanged();
                Refresh();
            }
        };
        Refresh();
    }

    public void Refresh()
    {
        Entries.Clear();
        var project = _sidebar.CurrentProject;
        if (project is null)
        {
            OnPropertyChanged(nameof(EntryCount));
            return;
        }
        foreach (var entry in project.Memories.OrderByDescending(entry => entry.UpdatedAt))
        {
            Entries.Add(new MemoryEntryViewModel(entry, this));
        }
        OnPropertyChanged(nameof(EntryCount));
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        var project = _sidebar.CurrentProject;
        if (project is null)
        {
            ErrorMessage = "请先选择一个项目。";
            return;
        }

        // Reuse the same validation the agent path uses (secret
        // detection, user-memory confirmation) so a manual entry has
        // the same rules as one the agent tried to write.
        var service = new MemoryService();
        var request = new MemoryWriteRequest
        {
            ProjectId = project.Id,
            Category = NewCategory,
            Content = NewContent,
            Source = "user",
            // User explicitly entered this in the modal — treat as
            // confirmed so the IsSafeUserMemory check doesn't block
            // arbitrary user memory.
            UserConfirmed = true,
        };
        var result = service.TryCreate(request);
        if (!result.IsStored || result.Entry is null)
        {
            ErrorMessage = result.Reason;
            return;
        }

        project.Memories.Add(result.Entry);
        await SaveAsync();
        NewContent = "";
        ErrorMessage = null;
        Refresh();
    }

    private bool CanAdd() =>
        IsAvailable
        && !string.IsNullOrWhiteSpace(NewContent)
        && ErrorMessage is null or "";

    partial void OnNewContentChanged(string value)
    {
        // Clear the error when the user starts typing again so the
        // red message doesn't linger after they fix the issue.
        if (ErrorMessage is not null)
        {
            ErrorMessage = null;
        }
        AddCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task DeleteAsync(MemoryEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }
        var project = _sidebar.CurrentProject;
        if (project is null)
        {
            return;
        }
        project.Memories.Remove(entry.Source);
        await SaveAsync();
        Refresh();
    }

    // Exposed so the per-row Delete button can resolve which editor
    // owns each row (the XAML stays declarative — no need to bubble
    // up to the ItemsControl's DataContext).
    internal Task DeleteEntryAsync(MemoryEntryViewModel entry) => DeleteAsync(entry);

    // Persist the current project (with the updated Memories list)
    // back to the repository. The repo's contract is "save the full
    // list of projects" — same pattern AgentRunnerViewModel uses
    // after an agent run adds memory.
    private async Task SaveAsync()
    {
        var projects = (await _repository.LoadProjectsAsync()).ToList();
        var index = projects.FindIndex(project => project.Id == _sidebar.CurrentProject?.Id);
        if (index >= 0 && _sidebar.CurrentProject is not null)
        {
            projects[index] = _sidebar.CurrentProject;
        }
        else if (_sidebar.CurrentProject is not null)
        {
            projects.Add(_sidebar.CurrentProject);
        }
        await _repository.SaveProjectsAsync(projects);
    }
}

// Wrapper around a single MemoryEntry. Holds the source record so
// the editor can mutate the underlying list (delete) without
// re-querying the repo; exposes display strings so the XAML can
// stay declarative.
//
// The wrapper carries a back-reference to its editor so the per-row
// Delete button can just bind to its own DeleteCommand — no need
// for the XAML to bubble up to the parent ItemsControl's
// DataContext to find the right DeleteCommand on the editor.
public sealed partial class MemoryEntryViewModel
{
    public MemoryEntry Source { get; }
    public string CategoryDisplay { get; }
    public string ContentPreview { get; }
    public string UpdatedAtDisplay { get; }

    private readonly MemoryEditorViewModel _editor;

    public MemoryEntryViewModel(MemoryEntry source, MemoryEditorViewModel editor)
    {
        Source = source;
        _editor = editor;
        CategoryDisplay = source.Category.ToString();
        ContentPreview = source.Content.Length > 240
            ? source.Content[..240] + "…"
            : source.Content;
        UpdatedAtDisplay = source.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    [RelayCommand]
    private Task DeleteAsync() => _editor.DeleteEntryAsync(this);
}
