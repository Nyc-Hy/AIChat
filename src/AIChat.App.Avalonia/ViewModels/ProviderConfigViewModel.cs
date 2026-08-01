using System.Collections.ObjectModel;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Llm.Routing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the "model provider" UI surface: input fields, template dropdown,
// advanced provider list, save and test commands.
//
// PR-2 scope: pure extraction from MainWindowViewModel. The view-model
// talks to the parent only through the events below; it does not poke
// at MainWindowViewModel's Activity, StatusMessage, or IsRunning fields
// directly. That keeps the cross-VM surface narrow and easy to mock.
public sealed partial class ProviderConfigViewModel : ViewModelBase
{
    private readonly IAppRepository _repository;
    private readonly ProviderConnectionTester _tester;
    private readonly ISettingsHolder _settingsHolder;

    public event EventHandler<ProviderSavedEventArgs>? Saved;
    public event EventHandler<ProviderTestStartedEventArgs>? TestStarted;
    public event EventHandler<ProviderTestCompletedEventArgs>? TestCompleted;

    [ObservableProperty]
    private string providerApiKey = "";

    [ObservableProperty]
    private string providerModel = "";

    [ObservableProperty]
    private string providerBaseUrl = "";

    [ObservableProperty]
    private ProviderTemplateViewModel? selectedProviderTemplate;

    // Last "测试连接" result, surfaced inline in SettingsView so the
    // user doesn't have to look at the conversation-feed bubble
    // (which is hidden behind the modal) or the bottom status bar
    // (which auto-clears on the next event). Cleared when a new
    // test starts so the row doesn't show stale text.
    [ObservableProperty]
    private string lastTestMessage = "";

    [ObservableProperty]
    private bool lastTestIsSuccess;

    [ObservableProperty]
    private bool lastTestHasResult;

    [ObservableProperty]
    private bool isTestInFlight;

    public ObservableCollection<ProviderTemplateViewModel> ProviderTemplates { get; } = [];
    public ObservableCollection<ProviderCardViewModel> AdvancedProviders { get; } = [];

    public ProviderConfigViewModel(
        IAppRepository repository,
        ProviderConnectionTester tester,
        ISettingsHolder settingsHolder)
    {
        _repository = repository;
        _tester = tester;
        _settingsHolder = settingsHolder;
    }

    // Populates the dropdowns and the active-provider list from the
    // current settings. Called by the parent whenever the underlying
    // settings change (initial load, after save).
    public void Refresh()
    {
        var active = ProviderSettingsService.GetSelectedProvider(_settingsHolder.Current);
        PopulateAdvancedProviders(active);
        PopulateProviderTemplates(active);
    }

    [RelayCommand]
    private async Task SaveProviderAsync()
    {
        if (SelectedProviderTemplate is null)
        {
            Saved?.Invoke(this, new ProviderSavedEventArgs
            {
                Settings = _settingsHolder.Current,
                ProviderName = "",
                ModelId = "",
                AlreadyExisted = false,
                ErrorMessage = "请选择模型提供方。"
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(ProviderApiKey))
        {
            Saved?.Invoke(this, new ProviderSavedEventArgs
            {
                Settings = _settingsHolder.Current,
                ProviderName = SelectedProviderTemplate.Name,
                ModelId = "",
                AlreadyExisted = false,
                ErrorMessage = "请输入 API Key。"
            });
            return;
        }

        var settings = _settingsHolder.Current;
        var result = ProviderSettingsService.AddConfiguredProvider(
            settings,
            SelectedProviderTemplate.Id,
            ProviderApiKey);
        if (!string.IsNullOrWhiteSpace(ProviderBaseUrl))
        {
            result.Provider.BaseUrl = ProviderBaseUrl.Trim();
        }

        if (!string.IsNullOrWhiteSpace(ProviderModel))
        {
            result.Provider.SelectedModelId = ChatProviderCatalog.ResolveModel(
                result.Provider.TemplateId,
                ProviderModel.Trim()).Id;
        }

        ProviderSettingsService.ApplySelectedProvider(settings);
        ProviderSettingsService.NormalizeModelParameters(settings);
        await _repository.SaveSettingsAsync(settings);

        ProviderApiKey = "";
        var active = ProviderSettingsService.GetSelectedProvider(settings);
        PopulateAdvancedProviders(active);
        PopulateProviderTemplates(active);

        Saved?.Invoke(this, new ProviderSavedEventArgs
        {
            Settings = settings,
            ProviderName = result.Provider.Name,
            ModelId = result.Provider.SelectedModelId,
            AlreadyExisted = result.AlreadyExisted
        });
    }

    [RelayCommand]
    private async Task TestProviderAsync()
    {
        var settings = _settingsHolder.Current;
        var configured = ProviderSettingsService.GetSelectedProvider(settings);
        if (configured is null || string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            LastTestMessage = "没有可测试的 API Key — 先保存配置。";
            LastTestIsSuccess = false;
            LastTestHasResult = true;
            TestCompleted?.Invoke(this, new ProviderTestCompletedEventArgs
            {
                IsSuccess = false,
                Message = "没有可测试的 API Key。"
            });
            return;
        }

        IsTestInFlight = true;
        LastTestHasResult = false;
        TestStarted?.Invoke(this, new ProviderTestStartedEventArgs
        {
            ProviderName = configured.Name,
            ModelId = configured.SelectedModelId
        });

        try
        {
            var result = await _tester.TestAsync(configured);
            LastTestMessage = result.Message;
            LastTestIsSuccess = result.IsSuccess;
            LastTestHasResult = true;
            TestCompleted?.Invoke(this, new ProviderTestCompletedEventArgs
            {
                IsSuccess = result.IsSuccess,
                Message = result.Message
            });
        }
        catch (Exception ex)
        {
            LastTestMessage = ex.Message;
            LastTestIsSuccess = false;
            LastTestHasResult = true;
            TestCompleted?.Invoke(this, new ProviderTestCompletedEventArgs
            {
                IsSuccess = false,
                Message = ex.Message,
                Exception = ex
            });
        }
        finally
        {
            IsTestInFlight = false;
        }
    }

    private void PopulateAdvancedProviders(ConfiguredLlmProvider? active)
    {
        AdvancedProviders.Clear();
        foreach (var provider in ChatProviderCatalog.All)
        {
            var isActive = active is not null &&
                           string.Equals(active.TemplateId, provider.Id, StringComparison.OrdinalIgnoreCase);
            AdvancedProviders.Add(new ProviderCardViewModel(
                provider.Name,
                provider.DefaultModel,
                isActive ? "当前" : "可用",
                provider.Id,
                isActive,
                SelectTemplate));
        }
    }

    // Wired to the "可用的提供方" card SelectCommand. Finds the matching
    // template in the dropdown and assigns it; OnSelectedProviderTemplateChanged
    // then re-seeds the model / base-url inputs from the catalog defaults
    // (or the active provider's saved values if the user re-picks the
    // same provider). The user types an API key + clicks Save to commit.
    private void SelectTemplate(string templateId)
    {
        var template = ProviderTemplates.FirstOrDefault(t =>
            string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));
        if (template is not null)
        {
            SelectedProviderTemplate = template;
        }
    }

    private void PopulateProviderTemplates(ConfiguredLlmProvider? active)
    {
        ProviderTemplates.Clear();
        foreach (var provider in ChatProviderCatalog.All)
        {
            ProviderTemplates.Add(new ProviderTemplateViewModel(
                provider.Id,
                provider.Name,
                provider.DefaultBaseUrl,
                provider.DefaultModel));
        }

        SelectedProviderTemplate = ProviderTemplates.FirstOrDefault(template =>
                                       string.Equals(template.Id, active?.TemplateId, StringComparison.OrdinalIgnoreCase)) ??
                                   ProviderTemplates.FirstOrDefault();
        ApplySelectedProviderTemplateDefaults(active);
    }

    partial void OnSelectedProviderTemplateChanged(ProviderTemplateViewModel? value)
    {
        ApplySelectedProviderTemplateDefaults(ProviderSettingsService.GetSelectedProvider(_settingsHolder.Current));
    }

    private void ApplySelectedProviderTemplateDefaults(ConfiguredLlmProvider? active)
    {
        if (SelectedProviderTemplate is null)
        {
            ProviderBaseUrl = "";
            ProviderModel = "";
            return;
        }

        var useActiveProvider = active is not null &&
                                string.Equals(active.TemplateId, SelectedProviderTemplate.Id, StringComparison.OrdinalIgnoreCase);
        ProviderBaseUrl = useActiveProvider ? active!.BaseUrl : SelectedProviderTemplate.DefaultBaseUrl;
        ProviderModel = useActiveProvider ? active!.SelectedModelId : SelectedProviderTemplate.DefaultModel;
    }
}
