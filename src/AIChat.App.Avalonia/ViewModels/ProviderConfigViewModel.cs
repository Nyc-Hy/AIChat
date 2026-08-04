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

    // 2026-08-04: the actionable "what to do next" hint
    // for the most recent failed test. Surfaced in
    // SettingsView under the test result row so a
    // 401 / 404 / 429 doesn't just say "API Key 无效
    // 或缺失" with no next step — the user sees e.g.
    // "如果是 Coding Plan (sk-cp-…) 订阅，可能不覆盖
    // 当前模型——在 Settings 里换到 M2.7 试试".
    // Empty when the test succeeded or the error
    // had no actionable fix (5xx — the right answer
    // is "wait and retry" which the title alone
    // carries).
    [ObservableProperty]
    private string lastTestRemediationHint = "";

    // 2026-08-04: when the failure is "key valid but
    // wrong model for your tier" (the Coding Plan
    // + M3 case), the SettingsView shows a 1-click
    // "切到 M2.7 试试" button so the user doesn't
    // have to scroll to the model dropdown
    // themselves. Null when there's no
    // tier-compatibility hint to surface.
    [ObservableProperty]
    private string? lastTestSuggestedModelId = "";

    [ObservableProperty]
    private bool isTestInFlight;

    [ObservableProperty]
    private string lastSaveWarning = "";

    // 2026-08-04: 1-click "切到 M2.7 试试" affordance
    // wired to the failure-row button. The user
    // hits "测试连接", gets 401, sees the hint
    // pointing at M2.7, and clicks once to land
    // there without scrolling to the model
    // dropdown. The command takes the suggested
    // model id from LastTestSuggestedModelId and
    // applies it to the configured provider +
    // sets the model textbox so the change is
    // visible. Key + baseUrl are preserved —
    // we're not editing the connection, just
    // the model.
    [RelayCommand]
    private void ApplySuggestedModel(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }
        ProviderModel = modelId;
        // The next /save would commit it; we don't
        // auto-save because the user may want to
        // review the swap (or back out via the
        // existing dropdown) before persisting.
        // Marking LastTestRemediationHint null
        // hides the failure block so the next
        // test starts clean.
        LastTestRemediationHint = "";
        LastTestSuggestedModelId = "";
    }

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
        await _repository.SaveSettingsWithSecretsAsync(settings);

        LastSaveWarning = string.Equals(result.Provider.ApiKeyProtection, "session-only", StringComparison.OrdinalIgnoreCase)
            ? "系统凭据库不可用：密钥只在本次运行中有效，重启后需要重新输入。密钥没有写入明文文件。"
            : "";

        ProviderApiKey = "";
        var active = ProviderSettingsService.GetSelectedProvider(settings);
        PopulateAdvancedProviders(active);
        PopulateProviderTemplates(active);

        Saved?.Invoke(this, new ProviderSavedEventArgs
        {
            Settings = settings,
            ProviderName = result.Provider.Name,
            ModelId = result.Provider.SelectedModelId,
            AlreadyExisted = result.AlreadyExisted,
            WarningMessage = string.IsNullOrEmpty(LastSaveWarning) ? null : LastSaveWarning
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
            // 2026-08-04: surface the actionable
            // remediation hint and the
            // tier-compat "switch to M2.7" shortcut.
            // The hint is filled in by
            // ProviderErrorClassifier from the
            // error kind; the suggested model is
            // inferred from the configured model +
            // error kind (auth + M3 → M2.7; model
            // not found → leave null; transient →
            // null). Both surface in SettingsView
            // below the test result row.
            if (result.IsSuccess || result.ErrorInfo is null)
            {
                LastTestRemediationHint = "";
                LastTestSuggestedModelId = "";
            }
            else
            {
                LastTestRemediationHint = result.ErrorInfo.RemediationHint;
                LastTestSuggestedModelId = InferSuggestedFallbackModel(
                    configured.SelectedModelId,
                    result.ErrorInfo.Kind);
            }
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
                isActive
                    ? string.IsNullOrWhiteSpace(active!.ApiKey) ? "未配置" : "当前"
                    : "已支持",
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

    // 2026-08-04: pick a fallback model to suggest in
    // the test-failure UI when the configured model
    // is the cause of the error. The dominant case
    // is "key works for M2.7 but not M3" (Coding
    // Plan tier coverage) — a 1-click "切到 M2.7 试试"
    // button on the failure row is the difference
    // between a daily driver reading a 401 in 10
    // seconds and figuring it out in 10 minutes.
    // For transient / network / config errors, the
    // hint is "wait / check network / fix settings" —
    // no model swap helps, so the suggestion is null.
    //
    // `internal` so the ProviderConfigViewModelTests
    // test class can pin the routing logic without
    // booting a test surface to drive the public
    // method. The test class reaches the helper
    // through a thin shim (InferSuggestedFallbackModelForTest)
    // so the public surface stays clean.
    internal static string? InferSuggestedFallbackModel(string currentModelId, ProviderErrorKind errorKind)
    {
        // 401 / 403 / 404 → the model the user picked
        // is the wrong fit for their key. The "go down
        // a tier" rule: if the user is on the
        // flagship and we have a lower-tier that the
        // older Coding Plan covered, suggest the
        // lower-tier (M2.7). If the user is already
        // on M2.7, there's no lower tier to fall
        // back to — return null and let the
        // remediation hint carry the user to the
        // platform's billing dashboard.
        if (errorKind is not (ProviderErrorKind.Authentication
                            or ProviderErrorKind.PermissionDenied
                            or ProviderErrorKind.ModelNotFound))
        {
            return null;
        }
        if (string.Equals(currentModelId, "MiniMax-M3", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(currentModelId, "MiniMax-M3-highspeed", StringComparison.OrdinalIgnoreCase))
        {
            return "MiniMax-M2.7";
        }
        return null;
    }
}
