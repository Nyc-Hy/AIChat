using AIChat.Domain.Memory;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    public string ProjectMemorySearchText
    {
        get => _projectMemorySearchText;
        set
        {
            if (SetProperty(ref _projectMemorySearchText, value))
            {
                LoadProjectMemories();
            }
        }
    }

    public string ProjectMemoryFilterId
    {
        get => _projectMemoryFilterId;
        set
        {
            if (SetProperty(ref _projectMemoryFilterId, string.IsNullOrWhiteSpace(value) ? "all" : value))
            {
                LoadProjectMemories();
            }
        }
    }

    public bool HasProjectMemories => ProjectMemories.Count > 0;
    public string ProjectMemorySummary
    {
        get
        {
            var project = SelectedProject?.Project;
            if (project is null)
            {
                return "未选择项目";
            }

            return $"{project.Memories.Count} 条已保存 · {project.PendingMemories.Count} 条待确认";
        }
    }

    private void LoadProjectMemories()
    {
        _projectMemoryClearArmed = false;
        ProjectMemories.Clear();
        var project = SelectedProject?.Project;
        if (project is null)
        {
            RaiseProjectMemoryChanges();
            return;
        }

        var items = project.PendingMemories
            .Select(memory => new ProjectMemoryViewModel(memory, isPending: true))
            .Concat(project.Memories.Select(memory => new ProjectMemoryViewModel(memory, isPending: false)))
            .Where(MatchesProjectMemoryFilter)
            .Where(MatchesProjectMemorySearch)
            .OrderByDescending(memory => memory.IsPending)
            .ThenByDescending(memory => memory.Memory.UpdatedAt)
            .ToList();

        foreach (var item in items)
        {
            ProjectMemories.Add(item);
        }

        RaiseProjectMemoryChanges();
    }

    private bool MatchesProjectMemoryFilter(ProjectMemoryViewModel memory)
    {
        return ProjectMemoryFilterId switch
        {
            "pending" => memory.IsPending,
            "project" => memory.Category == MemoryCategory.Project && !memory.IsPending,
            "task" => memory.Category == MemoryCategory.Task && !memory.IsPending,
            "tool" => memory.Category == MemoryCategory.Tool && !memory.IsPending,
            "user" => memory.Category == MemoryCategory.User && !memory.IsPending,
            _ => true
        };
    }

    private bool MatchesProjectMemorySearch(ProjectMemoryViewModel memory)
    {
        if (string.IsNullOrWhiteSpace(ProjectMemorySearchText))
        {
            return true;
        }

        var query = ProjectMemorySearchText.Trim();
        return memory.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               memory.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               memory.MetadataText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private async Task AcceptProjectMemoryAsync(ProjectMemoryViewModel? memory)
    {
        if (SelectedProject is null || memory is not { IsPending: true })
        {
            return;
        }

        var project = SelectedProject.Project;
        var pending = project.PendingMemories.FirstOrDefault(item => item.Id == memory.Id);
        if (pending is null)
        {
            return;
        }

        if (!HasSimilarMemory(project.Memories, pending.Category, pending.Content))
        {
            pending.UpdatedAt = DateTimeOffset.Now;
            project.Memories.Add(pending);
        }

        project.PendingMemories.Remove(pending);
        project.UpdatedAt = DateTimeOffset.Now;
        await SaveProjectsAsync();
        LoadProjectMemories();
        RaiseProjectLoadSnapshotProperties();
        StatusText = "记忆已确认保存";
    }

    private async Task RemoveProjectMemoryAsync(ProjectMemoryViewModel? memory)
    {
        if (SelectedProject is null || memory is null)
        {
            return;
        }

        var project = SelectedProject.Project;
        var removed = memory.IsPending
            ? project.PendingMemories.RemoveAll(item => item.Id == memory.Id)
            : project.Memories.RemoveAll(item => item.Id == memory.Id);
        if (removed == 0)
        {
            return;
        }

        project.UpdatedAt = DateTimeOffset.Now;
        await SaveProjectsAsync();
        LoadProjectMemories();
        RaiseProjectLoadSnapshotProperties();
        StatusText = memory.IsPending ? "待确认记忆已删除" : "记忆已删除";
    }

    private async Task DeduplicateProjectMemoriesAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var project = SelectedProject.Project;
        var before = project.Memories.Count;
        project.Memories = project.Memories
            .GroupBy(memory => $"{memory.Category}:{NormalizeMemoryContent(memory.Content)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(memory => memory.UpdatedAt).First())
            .OrderByDescending(memory => memory.UpdatedAt)
            .ToList();
        var removed = before - project.Memories.Count;
        if (removed == 0)
        {
            StatusText = "没有重复记忆";
            return;
        }

        project.UpdatedAt = DateTimeOffset.Now;
        await SaveProjectsAsync();
        LoadProjectMemories();
        RaiseProjectLoadSnapshotProperties();
        StatusText = $"已清理 {removed} 条重复记忆";
    }

    private async Task ClearProjectMemoriesAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var project = SelectedProject.Project;
        var count = project.Memories.Count + project.PendingMemories.Count;
        if (count == 0)
        {
            return;
        }

        if (!_projectMemoryClearArmed)
        {
            _projectMemoryClearArmed = true;
            StatusText = $"再次点击“清空”将删除当前项目 {count} 条记忆";
            return;
        }

        project.Memories.Clear();
        project.PendingMemories.Clear();
        _projectMemoryClearArmed = false;
        project.UpdatedAt = DateTimeOffset.Now;
        await SaveProjectsAsync();
        LoadProjectMemories();
        RaiseProjectLoadSnapshotProperties();
        StatusText = $"已清空 {count} 条记忆";
    }

    private void RaiseProjectMemoryChanges()
    {
        OnPropertyChanged(nameof(ProjectMemories));
        OnPropertyChanged(nameof(HasProjectMemories));
        OnPropertyChanged(nameof(ProjectMemorySummary));
        AcceptProjectMemoryCommand.RaiseCanExecuteChanged();
        RemoveProjectMemoryCommand.RaiseCanExecuteChanged();
        DeduplicateProjectMemoriesCommand.RaiseCanExecuteChanged();
        ClearProjectMemoriesCommand.RaiseCanExecuteChanged();
    }
}
