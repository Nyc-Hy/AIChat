using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Row in the provider list in the settings modal. Each card shows the
// provider's display name + default model + a truthful status badge
// ("当前", "未配置", or "已支持"). Clicking the card
// SelectCommand switches the template dropdown at the top of the modal
// to that provider, which through OnSelectedProviderTemplateChanged
// re-seeds the model / base-url inputs from the catalog defaults (or
// from the active provider's saved values when the user re-picks the
// provider they're already on). The status badge stays AccentBrush
// colored because the row really IS a target the user can act on —
// the earlier "record with no Click handler" shape was misleading: the
// accent foreground suggested interactivity the row didn't have.
public sealed class ProviderCardViewModel
{
    public string Name { get; }
    public string DefaultModel { get; }
    public string Status { get; }
    public string TemplateId { get; }
    public bool IsActive { get; }
    public ICommand SelectCommand { get; }

    public ProviderCardViewModel(
        string name,
        string defaultModel,
        string status,
        string templateId,
        bool isActive,
        Action<string> selectTemplate)
    {
        Name = name;
        DefaultModel = defaultModel;
        Status = status;
        TemplateId = templateId;
        IsActive = isActive;
        SelectCommand = new RelayCommand(() => selectTemplate(templateId));
    }
}
