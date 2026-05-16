using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Configuration;
using AIChat.Application.Llm.Routing;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    public string ModelName => SelectedConfiguredProvider is null
        ? "未配置模型"
        : $"{SelectedConfiguredProvider.Name} · {SelectedConfiguredProvider.SelectedModelId}";
    public IReadOnlyList<ConfiguredLlmProvider> ConfiguredProviders => Settings.ConfiguredProviders;
    public ConfiguredLlmProvider? SelectedConfiguredProvider => ProviderSettingsService.GetSelectedProvider(Settings);
    public IReadOnlyList<ModelOptionItem> ActiveModelOptions
    {
        get
        {
            var items = new List<ModelOptionItem>();
            var selectedProviderId = Settings.ActiveConfiguredProviderId;
            foreach (var configured in Settings.ConfiguredProviders)
            {
                if (string.IsNullOrWhiteSpace(configured.ApiKey))
                {
                    continue;
                }

                // Filter by selected provider
                if (!string.IsNullOrEmpty(selectedProviderId) && configured.Id != selectedProviderId)
                {
                    continue;
                }

                var template = ChatProviderCatalog.Resolve(configured.TemplateId);
                foreach (var model in template.Models)
                {
                    items.Add(new ModelOptionItem(
                        $"{configured.TemplateId}|{model.Id}",
                        $"[{template.Name}] {model.DisplayName}"));
                }
            }

            return items;
        }
    }
    public bool HasModelParameterOptions => ModelParameterOptions.Count > 0;
    public string ActiveModelCapabilitySummary
    {
        get
        {
            var configured = SelectedConfiguredProvider;
            if (configured is null)
            {
                return "未选择模型";
            }

            var model = ChatProviderCatalog.ResolveModel(configured.TemplateId, configured.SelectedModelId);
            var label = string.IsNullOrWhiteSpace(model.CapabilityLabel)
                ? "标准聊天能力"
                : model.CapabilityLabel;
            return configured.SupportsVisionOverride && model.Capabilities.SupportsVision == false
                ? label + " · vision override"
                : label;
        }
    }
    public bool ActiveModelSupportsTools
    {
        get
        {
            var configured = SelectedConfiguredProvider;
            if (configured is null) return false;
            var model = ChatProviderCatalog.ResolveModel(configured.TemplateId, configured.SelectedModelId);
            return model.Capabilities?.SupportsTools == true;
        }
    }
    public bool ActiveModelSupportsVision
    {
        get
        {
            var configured = SelectedConfiguredProvider;
            if (configured is null) return false;
            var model = ChatProviderCatalog.ResolveModel(configured.TemplateId, configured.SelectedModelId);
            return model.Capabilities?.SupportsVision == true || configured.SupportsVisionOverride;
        }
    }
    public bool SelectedConfiguredProviderSupportsVisionOverride
    {
        get => SelectedConfiguredProvider?.SupportsVisionOverride == true;
        set
        {
            var configured = SelectedConfiguredProvider;
            if (configured is null || configured.SupportsVisionOverride == value)
            {
                return;
            }

            configured.SupportsVisionOverride = value;
            ApplySelectedConfiguredProvider();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveModelSupportsVision));
            OnPropertyChanged(nameof(ActiveModelCapabilitySummary));
            RebuildCurrentInputArtifacts();
            _ = PersistSettingsQuietlyAsync();
        }
    }
    public bool HasApiKey => SelectedConfiguredProvider is not null && !string.IsNullOrWhiteSpace(SelectedConfiguredProvider.ApiKey);
    public string SelectedProviderId
    {
        get => Settings.ProviderId;
        set
        {
            if (Settings.ProviderId == value)
            {
                return;
            }

            ProviderSettingsService.SelectProviderTemplate(Settings, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(ModelName));
            OnPropertyChanged(nameof(SelectedActiveModelId));
            OnPropertyChanged(nameof(ActiveModelOptions));
            RebuildModelParameterOptions();
            UpdateContextUsage();
            _ = PersistSettingsQuietlyAsync();
        }
    }

    public string SelectedConfiguredProviderId
    {
        get => Settings.ActiveConfiguredProviderId;
        set
        {
            if (Settings.ActiveConfiguredProviderId == value)
            {
                return;
            }

            Settings.ActiveConfiguredProviderId = value;
            ProviderSettingsService.ApplySelectedProvider(Settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(SelectedConfiguredProvider));
            OnPropertyChanged(nameof(ModelName));
            OnPropertyChanged(nameof(SelectedActiveModelId));
            OnPropertyChanged(nameof(ActiveModelOptions));
            RebuildModelParameterOptions();
            UpdateContextUsage();
            RemoveConfiguredProviderCommand.RaiseCanExecuteChanged();
            _ = PersistSettingsQuietlyAsync();
        }
    }

    public string SelectedActiveModelId
    {
        get
        {
            var configured = SelectedConfiguredProvider;
            return configured is null ? "" : $"{configured.TemplateId}|{configured.SelectedModelId}";
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!ProviderSettingsService.SelectActiveModel(Settings, value))
            {
                return;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(ModelName));
            OnPropertyChanged(nameof(SelectedConfiguredProvider));
            OnPropertyChanged(nameof(SelectedConfiguredProviderSupportsVisionOverride));
            RebuildModelParameterOptions();
            RebuildCurrentInputArtifacts();
            UpdateContextUsage();
            _ = PersistSettingsQuietlyAsync();
        }
    }

    public string NewProviderApiKey
    {
        get => _newProviderApiKey;
        set
        {
            if (SetProperty(ref _newProviderApiKey, value))
            {
                AddConfiguredProviderCommand.RaiseCanExecuteChanged();
                TestProviderConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewProviderTemplateId
    {
        get => _newProviderTemplateId;
        set
        {
            if (SetProperty(ref _newProviderTemplateId, value))
            {
                // Also sync to SelectedProviderId for backward compatibility.
                SelectedProviderId = value;
            }
        }
    }

    public bool IsNewProviderApiKeyVisible
    {
        get => _isNewProviderApiKeyVisible;
        set => SetProperty(ref _isNewProviderApiKeyVisible, value);
    }

    public bool IsTestingProviderConnection
    {
        get => _isTestingProviderConnection;
        private set
        {
            if (SetProperty(ref _isTestingProviderConnection, value))
            {
                TestProviderConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void NormalizeProviderSettings()
    {
        ProviderSettingsService.Normalize(Settings, AgentDefaultTemperature);
        OnPropertyChanged(nameof(SelectedProviderId));
        OnPropertyChanged(nameof(SelectedActiveModelId));
        OnPropertyChanged(nameof(ActiveModelOptions));
        OnPropertyChanged(nameof(ConfiguredProviders));
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(SelectedConfiguredProviderId));
        OnPropertyChanged(nameof(SelectedConfiguredProvider));
        OnPropertyChanged(nameof(SelectedActiveModelId));
        OnPropertyChanged(nameof(ActiveModelOptions));
    }

    private void NormalizeModelParameters()
    {
        ProviderSettingsService.NormalizeModelParameters(Settings);
    }

    public sealed record ModelOptionItem(string Id, string DisplayName);

    private void RebuildModelParameterOptions()
    {
        var configured = SelectedConfiguredProvider;
        ModelParameterOptions.Clear();
        if (configured is null)
        {
            RaiseModelParameterOptionChanges();
            return;
        }

        var model = ChatProviderCatalog.ResolveModel(configured.TemplateId, configured.SelectedModelId);
        var values = ProviderSettingsService.NormalizeModelParameterValues(configured.TemplateId, model.Id, configured.ModelParameters);
        configured.ModelParameters = values;
        Settings.ModelParameters = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in model.Parameters)
        {
            var option = new ModelParameterOptionViewModel
            {
                Id = parameter.Id,
                Name = parameter.DisplayName,
                Description = parameter.Description,
                SelectedValue = values.TryGetValue(parameter.Id, out var value) ? value : parameter.DefaultValue
            };
            foreach (var parameterOption in parameter.Options)
            {
                option.Options.Add(parameterOption);
            }

            ModelParameterOptions.Add(option);
        }

        RaiseModelParameterOptionChanges();
    }

    private void SyncModelParameterOptionsToSettings()
    {
        var values = ModelParameterOptions.ToDictionary(
            parameter => parameter.Id,
            parameter => parameter.SelectedValue,
            StringComparer.OrdinalIgnoreCase);
        Settings.ModelParameters = values;
        var configured = SelectedConfiguredProvider;
        if (configured is not null)
        {
            configured.ModelParameters = ProviderSettingsService.NormalizeModelParameterValues(
                configured.TemplateId,
                configured.SelectedModelId,
                values);
        }
    }

    private void RaiseModelParameterOptionChanges()
    {
        OnPropertyChanged(nameof(ModelParameterOptions));
        OnPropertyChanged(nameof(HasModelParameterOptions));
        OnPropertyChanged(nameof(ActiveModelCapabilitySummary));
        OnPropertyChanged(nameof(ActiveModelSupportsTools));
        OnPropertyChanged(nameof(ActiveModelSupportsVision));
        OnPropertyChanged(nameof(SelectedConfiguredProviderSupportsVisionOverride));
    }

    private async Task AddConfiguredProviderAsync()
    {
        var result = ProviderSettingsService.AddConfiguredProvider(
            Settings,
            _newProviderTemplateId,
            NewProviderApiKey);
        NewProviderApiKey = "";
        await _repository.SaveSettingsAsync(Settings);
        RaiseConfiguredProviderChanges();
        StatusText = result.AlreadyExisted
            ? "该模型提供商已存在，已切换到该配置"
            : $"{result.Provider.Name} 已添加";
    }

    private async Task TestProviderConnectionAsync()
    {
        var apiKey = NewProviderApiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText = "请先输入 API Key";
            return;
        }

        var template = ChatProviderCatalog.Resolve(_newProviderTemplateId);
        var provider = new ConfiguredLlmProvider
        {
            TemplateId = template.Id,
            ProtocolId = template.ProtocolId,
            Name = template.Name,
            BaseUrl = template.DefaultBaseUrl,
            ApiKey = apiKey,
            SelectedModelId = template.DefaultModel,
            ModelParameters = ProviderSettingsService.NormalizeModelParameterValues(template.Id, template.DefaultModel, null)
        };
        IsTestingProviderConnection = true;
        StatusText = "正在测试模型连接...";
        try
        {
            var result = await new ProviderConnectionTester().TestAsync(provider);
            StatusText = result.IsSuccess ? "连接测试通过" : $"连接测试失败：{result.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"连接测试失败：{ex.Message}";
        }
        finally
        {
            IsTestingProviderConnection = false;
        }
    }

    private async Task RemoveConfiguredProviderAsync()
    {
        if (!ProviderSettingsService.RemoveSelectedProvider(Settings))
        {
            return;
        }

        await _repository.SaveSettingsAsync(Settings);
        RaiseConfiguredProviderChanges();
        StatusText = "模型提供商已移除";
    }

    private AppSettings? CreateEffectiveSettings()
    {
        return ProviderSettingsService.CreateEffectiveSettings(Settings, AgentDefaultTemperature);
    }

    private void ApplySelectedConfiguredProvider()
    {
        ProviderSettingsService.ApplySelectedProvider(Settings);
    }

    private void RaiseConfiguredProviderChanges()
    {
        OnPropertyChanged(nameof(ConfiguredProviders));
        OnPropertyChanged(nameof(SelectedConfiguredProvider));
        OnPropertyChanged(nameof(SelectedConfiguredProviderId));
        OnPropertyChanged(nameof(ActiveModelOptions));
        OnPropertyChanged(nameof(SelectedActiveModelId));
        OnPropertyChanged(nameof(SelectedConfiguredProviderSupportsVisionOverride));
        RebuildModelParameterOptions();
        RebuildCurrentInputArtifacts();
        OnPropertyChanged(nameof(ModelName));
        OnPropertyChanged(nameof(HasApiKey));
        RemoveConfiguredProviderCommand.RaiseCanExecuteChanged();
    }
}
