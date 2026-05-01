using System.Collections.ObjectModel;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentPlanViewModel : ObservableObject
{
    private readonly AgentPlan _plan;

    public AgentPlanViewModel(AgentPlan plan)
    {
        _plan = plan;
        Items = new ObservableCollection<AgentPlanItemViewModel>(
            plan.Items.OrderBy(item => item.Order).Select(item => new AgentPlanItemViewModel(item)));
    }

    public AgentPlan Plan => _plan;
    public string Summary => _plan.Summary;
    public int TotalItems => Items.Count;
    public int CompletedItems => Items.Count(item => item.Status == AgentPlanItemStatus.Completed);
    public string ProgressText => $"{CompletedItems}/{TotalItems} 完成";
    public bool HasItems => Items.Count > 0;

    public ObservableCollection<AgentPlanItemViewModel> Items { get; }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(CompletedItems));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(HasItems));
    }
}
