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
    }

    public string Id => _run.Id;
    public ObservableCollection<AgentStepViewModel> Steps { get; }
    public bool HasSteps => Steps.Count > 0;

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
}
