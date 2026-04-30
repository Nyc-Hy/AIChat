using System.Collections.ObjectModel;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentRunViewModel : ObservableObject
{
    private readonly AgentRun _run;

    public AgentRunViewModel(AgentRun run)
    {
        _run = run;
        Steps = new ObservableCollection<AgentStepViewModel>(
            run.Steps.OrderBy(step => step.Number).Select(step => new AgentStepViewModel(step)));
        FileChanges = new ObservableCollection<AgentFileChangeViewModel>(
            run.FileChanges.Select(change => new AgentFileChangeViewModel(change)));
    }

    public string Id => _run.Id;
    public ObservableCollection<AgentStepViewModel> Steps { get; }
    public ObservableCollection<AgentFileChangeViewModel> FileChanges { get; }
    public bool HasSteps => Steps.Count > 0;
    public bool HasFileChanges => FileChanges.Count > 0;
    public IReadOnlyList<string> ChangedPaths => FileChanges
        .Select(change => change.Path)
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    public string ChangeSummary => ChangedPaths.Count == 0
        ? "本轮没有记录文件变更。"
        : string.Join(Environment.NewLine, ChangedPaths.Select(path => $"- {path}"));

    public AgentStepViewModel AddStep(AgentStep step)
    {
        if (_run.Steps.All(item => item.Id != step.Id))
        {
            _run.Steps.Add(step);
        }

        var existing = Steps.FirstOrDefault(item => item.Id == step.Id);
        if (existing is not null)
        {
            existing.Refresh();
            return existing;
        }

        var viewModel = new AgentStepViewModel(step);
        Steps.Add(viewModel);
        OnPropertyChanged(nameof(HasSteps));
        return viewModel;
    }

    public void Complete(AgentRunStatus status)
    {
        _run.Status = status;
        _run.CompletedAt = DateTimeOffset.Now;
    }

    public void SyncFileChanges()
    {
        foreach (var change in _run.FileChanges)
        {
            if (FileChanges.Any(item => item.Id == change.Id))
            {
                continue;
            }

            FileChanges.Add(new AgentFileChangeViewModel(change));
        }

        OnPropertyChanged(nameof(HasFileChanges));
        OnPropertyChanged(nameof(ChangedPaths));
        OnPropertyChanged(nameof(ChangeSummary));
    }
}
