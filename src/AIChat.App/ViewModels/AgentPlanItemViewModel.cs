using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed class AgentPlanItemViewModel : ObservableObject
{
    private readonly AgentPlanItem _item;

    public AgentPlanItemViewModel(AgentPlanItem item)
    {
        _item = item;
    }

    public AgentPlanItem Item => _item;
    public string Title => _item.Title;
    public AgentPlanItemStatus Status => _item.Status;
    public string Notes => _item.Notes;
    public int Order => _item.Order;

    public string StatusText => _item.Status switch
    {
        AgentPlanItemStatus.Pending => "待办",
        AgentPlanItemStatus.InProgress => "进行中",
        AgentPlanItemStatus.Completed => "已完成",
        AgentPlanItemStatus.Blocked => "阻塞",
        AgentPlanItemStatus.Skipped => "跳过",
        _ => "待办"
    };

    public string StatusColor => _item.Status switch
    {
        AgentPlanItemStatus.Pending => "#98A2B0",
        AgentPlanItemStatus.InProgress => "#3B82F6",
        AgentPlanItemStatus.Completed => "#22C55E",
        AgentPlanItemStatus.Blocked => "#EF4444",
        AgentPlanItemStatus.Skipped => "#98A2B0",
        _ => "#98A2B0"
    };

    public bool HasNotes => !string.IsNullOrWhiteSpace(_item.Notes);
    public bool IsSkipped => _item.Status == AgentPlanItemStatus.Skipped;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(IsSkipped));
    }
}
