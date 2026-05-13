using AIChat.Application.Agents;

namespace AIChat.App.ViewModels;

public sealed class AgentSmokeTestItemViewModel : ObservableObject
{
    private bool _isChecked;

    public AgentSmokeTestItemViewModel(AgentSmokeTestItem item)
    {
        Item = item;
    }

    public AgentSmokeTestItem Item { get; }
    public string Title => Item.Title;
    public string Detail => Item.Detail;
    public AgentSmokeTestStatus Status => Item.Status;
    public string StatusText => Item.Status switch
    {
        AgentSmokeTestStatus.Passed => "已满足",
        AgentSmokeTestStatus.Blocked => "需处理",
        _ => "待确认"
    };

    public bool IsBlocked => Item.Status == AgentSmokeTestStatus.Blocked;
    public bool IsPassed => Item.Status == AgentSmokeTestStatus.Passed;

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}
