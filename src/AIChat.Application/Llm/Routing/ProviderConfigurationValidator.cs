using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;

namespace AIChat.Application.Llm.Routing;

public static class ProviderConfigurationValidator
{
    public static ProviderValidationResult ValidateEffectiveSettings(
        AppSettings? settings,
        bool requireTools = false,
        bool requireVision = false)
    {
        if (settings is null)
        {
            return new ProviderValidationResult(
            [
                Error("provider.missing", "请先在设置中添加模型提供商。")
            ]);
        }

        var issues = new List<ProviderValidationIssue>();
        ValidateCommon(
            settings.ProviderId,
            settings.ProtocolId,
            settings.ProviderName,
            settings.BaseUrl,
            settings.ApiKey,
            settings.Model,
            settings.Temperature,
            settings.MaxOutputTokens,
            settings.ModelContextLimit,
            settings.ModelParameters,
            requireApiKey: true,
            requireTools,
            requireVision,
            issues);
        return new ProviderValidationResult(issues);
    }

    public static ProviderValidationResult ValidateConfiguredProvider(
        ConfiguredLlmProvider provider,
        bool requireApiKey = true)
    {
        var issues = new List<ProviderValidationIssue>();
        ValidateCommon(
            provider.TemplateId,
            provider.ProtocolId,
            provider.Name,
            provider.BaseUrl,
            provider.ApiKey,
            provider.SelectedModelId,
            temperature: 0.3,
            maxOutputTokens: 4096,
            modelContextLimit: ChatProviderCatalog.ResolveModel(provider.TemplateId, provider.SelectedModelId).ContextLimit,
            provider.ModelParameters,
            requireApiKey,
            requireTools: false,
            requireVision: false,
            issues);
        return new ProviderValidationResult(issues);
    }

    private static void ValidateCommon(
        string providerId,
        string protocolId,
        string providerName,
        string baseUrl,
        string apiKey,
        string modelId,
        double temperature,
        int maxOutputTokens,
        int modelContextLimit,
        IDictionary<string, string> modelParameters,
        bool requireApiKey,
        bool requireTools,
        bool requireVision,
        List<ProviderValidationIssue> issues)
    {
        var provider = ChatProviderCatalog.Resolve(providerId);
        if (!string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Warning("provider.unknown", $"未知提供商“{providerName}”，已按 {provider.Name} 处理。"));
        }

        if (!string.Equals(provider.ProtocolId, protocolId, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Error("provider.protocol", $"{provider.Name} 的协议应为 {provider.ProtocolId}。"));
        }

        if (requireApiKey && string.IsNullOrWhiteSpace(apiKey))
        {
            issues.Add(Error("provider.api_key", $"还没有配置 {provider.Name} API Key。"));
        }

        if (!IsValidHttpUrl(baseUrl))
        {
            issues.Add(Error("provider.base_url", $"{provider.Name} Base URL 必须是有效的 http/https 地址。"));
        }

        var knownModel = provider.Models.FirstOrDefault(model =>
            string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
        if (knownModel is null)
        {
            issues.Add(Error("provider.model", $"{provider.Name} 不包含模型“{modelId}”。"));
            knownModel = provider.Models.FirstOrDefault();
        }

        if (temperature is < 0 or > 2)
        {
            issues.Add(Error("provider.temperature", "Temperature 必须在 0 到 2 之间。"));
        }

        if (maxOutputTokens <= 0)
        {
            issues.Add(Error("provider.max_output_tokens", "最大输出 tokens 必须大于 0。"));
        }

        if (modelContextLimit <= 0)
        {
            issues.Add(Error("provider.context_limit", "模型上下文长度必须大于 0。"));
        }

        if (requireTools && knownModel?.Capabilities.SupportsTools != true)
        {
            issues.Add(Error("provider.tools", $"模型 {knownModel?.Id ?? modelId} 不支持工具调用。"));
        }

        if (requireVision && knownModel?.Capabilities.SupportsVision != true)
        {
            issues.Add(Error("provider.vision", $"模型 {knownModel?.Id ?? modelId} 不支持图片输入。"));
        }

        var allowedParameters = knownModel?.Parameters
            .ToDictionary(parameter => parameter.Id, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, LlmModelParameterInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in modelParameters)
        {
            if (!allowedParameters.TryGetValue(parameter.Key, out var metadata))
            {
                issues.Add(Warning("provider.parameter.unknown", $"参数 {parameter.Key} 不适用于当前模型，将被忽略。"));
                continue;
            }

            if (metadata.Options.Count > 0 &&
                !string.IsNullOrWhiteSpace(parameter.Value) &&
                metadata.Options.All(option => !string.Equals(option.Value, parameter.Value, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(Warning("provider.parameter.invalid", $"参数 {metadata.DisplayName} 的值无效，将使用默认值。"));
            }
        }
    }

    private static bool IsValidHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               !string.IsNullOrWhiteSpace(uri.Host);
    }

    private static ProviderValidationIssue Error(string code, string message)
    {
        return new ProviderValidationIssue(ProviderValidationSeverity.Error, code, message);
    }

    private static ProviderValidationIssue Warning(string code, string message)
    {
        return new ProviderValidationIssue(ProviderValidationSeverity.Warning, code, message);
    }
}
