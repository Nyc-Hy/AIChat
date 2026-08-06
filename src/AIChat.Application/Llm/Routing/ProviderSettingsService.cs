using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;

namespace AIChat.Application.Llm.Routing;

public sealed record AddConfiguredProviderResult(ConfiguredLlmProvider Provider, bool AlreadyExisted);

public static class ProviderSettingsService
{
    public static ConfiguredLlmProvider? GetSelectedProvider(AppSettings settings)
    {
        return settings.ConfiguredProviders.FirstOrDefault(provider => provider.Id == settings.ActiveConfiguredProviderId) ??
               settings.ConfiguredProviders.FirstOrDefault();
    }

    public static void Normalize(AppSettings settings, double defaultTemperature)
    {
        var provider = ChatProviderCatalog.Resolve(settings.ProviderId);
        settings.ProviderId = provider.Id;
        settings.ProviderName = provider.Name;
        settings.ProtocolId = provider.ProtocolId;
        // 2026-08-02: BaseUrl is preserved when the user has a
        // valid http(s) URL. The previous behaviour was to
        // overwrite unconditionally with the provider's default,
        // which silently broke self-hosted users on every startup
        // (e.g. someone proxying MiniMax through a private gateway
        // at https://proxy.example.com/v1 would see their BaseUrl
        // reset to https://api.minimax.io/v1, then ship the next
        // message to the wrong endpoint and get an auth failure).
        // Falling back to the default only when the stored value
        // is missing or malformed keeps the migration safe for
        // self-hosted users without changing the default for
        // brand-new installs (where BaseUrl is empty by
        // construction).
        if (!IsValidHttpUrl(settings.BaseUrl))
        {
            settings.BaseUrl = provider.DefaultBaseUrl;
        }
        // 2026-08-02: If a 0.5 user upgrades with a stored BaseUrl that
        // points at a now-removed provider (Anthropic / DeepSeek / Xiaomi
        // MIMO), silently keeping that host would ship the next message to
        // the wrong endpoint and the user would see a 401 / 404 with no
        // explanation. Force-rewrite to the catalog default in that case;
        // self-hosted MiniMax-style proxies that share `api.minimax.io`'s
        // pattern are unaffected (their host is not in the legacy list).
        else if (IsLegacyProviderHost(settings.BaseUrl))
        {
            settings.BaseUrl = provider.DefaultBaseUrl;
        }
        settings.Temperature = defaultTemperature;

        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            settings.Model = provider.DefaultModel;
        }

        var model = ChatProviderCatalog.ResolveModel(provider.Id, settings.Model);
        settings.Model = model.Id;
        settings.ModelSupportsVision = model.Capabilities.SupportsVision;
        if (settings.ModelContextLimit <= 0 || settings.ModelContextLimit == provider.DefaultContextLimit)
        {
            settings.ModelContextLimit = model.ContextLimit;
        }

        foreach (var configured in settings.ConfiguredProviders)
        {
            NormalizeConfiguredProvider(configured);
        }

        DeduplicateConfiguredProviders(settings);

        if (settings.ConfiguredProviders.Count == 0 && !string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            settings.ConfiguredProviders.Add(new ConfiguredLlmProvider
            {
                TemplateId = provider.Id,
                ProtocolId = provider.ProtocolId,
                Name = provider.Name,
                BaseUrl = settings.BaseUrl,
                ApiKey = settings.ApiKey,
                SelectedModelId = settings.Model,
                ModelParameters = NormalizeModelParameterValues(provider.Id, settings.Model, settings.ModelParameters)
            });
        }

        EnsureActiveProvider(settings);
        ApplySelectedProvider(settings);
    }

    public static void NormalizeModelParameters(AppSettings settings)
    {
        var configured = GetSelectedProvider(settings);
        if (configured is null)
        {
            settings.ModelParameters = [];
            return;
        }

        configured.ModelParameters = NormalizeModelParameterValues(
            configured.TemplateId,
            configured.SelectedModelId,
            configured.ModelParameters);
        settings.ModelParameters = new Dictionary<string, string>(configured.ModelParameters, StringComparer.OrdinalIgnoreCase);
    }

    public static void SelectProviderTemplate(AppSettings settings, string providerId)
    {
        var provider = ChatProviderCatalog.Resolve(providerId);
        settings.ProviderId = provider.Id;
        settings.ProtocolId = provider.ProtocolId;
        settings.ProviderName = provider.Name;
        settings.BaseUrl = provider.DefaultBaseUrl;
        settings.Model = provider.DefaultModel;
        settings.ModelContextLimit = provider.DefaultContextLimit;
        settings.ModelSupportsVision = ChatProviderCatalog.ResolveModel(provider.Id, provider.DefaultModel)
            .Capabilities.SupportsVision;
    }

    public static bool SelectActiveModel(AppSettings settings, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('|', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        var templateId = parts[0];
        var modelId = parts[1];
        var configured = settings.ConfiguredProviders.FirstOrDefault(
            provider => string.Equals(provider.TemplateId, templateId, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(provider.ApiKey));
        if (configured is null)
        {
            return false;
        }

        settings.ActiveConfiguredProviderId = configured.Id;
        var model = ChatProviderCatalog.ResolveModel(templateId, modelId);
        configured.SelectedModelId = model.Id;
        configured.ModelParameters = NormalizeModelParameterValues(templateId, model.Id, configured.ModelParameters);
        ApplySelectedProvider(settings);
        return true;
    }

    public static AddConfiguredProviderResult AddConfiguredProvider(
        AppSettings settings,
        string templateId,
        string apiKey)
    {
        var template = ChatProviderCatalog.Resolve(templateId);
        var model = ChatProviderCatalog.ResolveModel(template.Id, template.DefaultModel);
        var trimmedApiKey = apiKey.Trim();
        var existing = settings.ConfiguredProviders.FirstOrDefault(provider =>
            string.Equals(provider.TemplateId, template.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(provider.ApiKey, trimmedApiKey, StringComparison.Ordinal));
        if (existing is not null)
        {
            settings.ActiveConfiguredProviderId = existing.Id;
            existing.ProtocolId = template.ProtocolId;
            existing.Name = template.Name;
            existing.BaseUrl = template.DefaultBaseUrl;
            existing.SelectedModelId = ChatProviderCatalog.ResolveModel(template.Id, existing.SelectedModelId).Id;
            existing.ModelParameters = NormalizeModelParameterValues(template.Id, existing.SelectedModelId, existing.ModelParameters);
            ApplySelectedProvider(settings);
            return new AddConfiguredProviderResult(existing, AlreadyExisted: true);
        }

        var configured = new ConfiguredLlmProvider
        {
            TemplateId = template.Id,
            ProtocolId = template.ProtocolId,
            Name = template.Name,
            BaseUrl = template.DefaultBaseUrl,
            ApiKey = trimmedApiKey,
            SelectedModelId = model.Id,
            ModelParameters = NormalizeModelParameterValues(template.Id, model.Id, null)
        };
        settings.ConfiguredProviders.Add(configured);
        settings.ActiveConfiguredProviderId = configured.Id;
        ApplySelectedProvider(settings);
        return new AddConfiguredProviderResult(configured, AlreadyExisted: false);
    }

    public static bool RemoveSelectedProvider(AppSettings settings)
    {
        var configured = GetSelectedProvider(settings);
        if (configured is null)
        {
            return false;
        }

        settings.ConfiguredProviders.Remove(configured);
        settings.ActiveConfiguredProviderId = settings.ConfiguredProviders.FirstOrDefault()?.Id ?? "";
        ApplySelectedProvider(settings);
        return true;
    }

    public static AppSettings? CreateEffectiveSettings(AppSettings settings, double defaultTemperature)
    {
        var configured = GetSelectedProvider(settings);
        if (configured is null || string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            return null;
        }

        var template = ChatProviderCatalog.Resolve(configured.TemplateId);
        var model = ChatProviderCatalog.ResolveModel(template.Id, configured.SelectedModelId);
        configured.TemplateId = template.Id;
        configured.ProtocolId = template.ProtocolId;
        configured.Name = template.Name;
        configured.BaseUrl = string.IsNullOrWhiteSpace(configured.BaseUrl)
            ? template.DefaultBaseUrl
            : configured.BaseUrl.Trim();
        configured.SelectedModelId = model.Id;

        return new AppSettings
        {
            ProviderId = configured.TemplateId,
            ProtocolId = configured.ProtocolId,
            ProviderName = configured.Name,
            BaseUrl = configured.BaseUrl,
            ApiKey = configured.ApiKey,
            Model = model.Id,
            Temperature = defaultTemperature,
            ModelContextLimit = model.ContextLimit,
            ModelSupportsVision = model.Capabilities.SupportsVision || configured.SupportsVisionOverride,
            ModelParameters = NormalizeModelParameterValues(configured.TemplateId, model.Id, configured.ModelParameters),
            ActiveConfiguredProviderId = configured.Id,
            AgentMaxToolRounds = settings.AgentMaxToolRounds,
            ConfiguredProviders = settings.ConfiguredProviders
        };
    }

    public static void ApplySelectedProvider(AppSettings settings)
    {
        var configured = GetSelectedProvider(settings);
        if (configured is null)
        {
            return;
        }

        var template = ChatProviderCatalog.Resolve(configured.TemplateId);
        var model = ChatProviderCatalog.ResolveModel(template.Id, configured.SelectedModelId);
        settings.ProviderId = configured.TemplateId;
        settings.ProtocolId = configured.ProtocolId;
        settings.ProviderName = configured.Name;
        settings.BaseUrl = string.IsNullOrWhiteSpace(configured.BaseUrl)
            ? template.DefaultBaseUrl
            : configured.BaseUrl.Trim();
        settings.ApiKey = configured.ApiKey;
        settings.Model = model.Id;
        settings.ModelContextLimit = model.ContextLimit;
        settings.ModelSupportsVision = model.Capabilities.SupportsVision || configured.SupportsVisionOverride;
        settings.ModelParameters = NormalizeModelParameterValues(configured.TemplateId, model.Id, configured.ModelParameters);
    }

    public static Dictionary<string, string> NormalizeModelParameterValues(
        string providerId,
        string modelId,
        IDictionary<string, string>? values)
    {
        var model = ChatProviderCatalog.ResolveModel(providerId, modelId);
        var known = model.Parameters.ToDictionary(parameter => parameter.Id, StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values is not null)
        {
            foreach (var entry in values)
            {
                if (!known.TryGetValue(entry.Key, out var parameter))
                {
                    continue;
                }

                var value = entry.Value ?? "";
                if (parameter.Options.Count > 0 &&
                    parameter.Options.All(option => !string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)))
                {
                    value = parameter.DefaultValue;
                }

                normalized[parameter.Id] = value;
            }
        }

        foreach (var parameter in model.Parameters)
        {
            normalized.TryAdd(parameter.Id, parameter.DefaultValue);
        }

        return normalized;
    }

    private static void NormalizeConfiguredProvider(ConfiguredLlmProvider configured)
    {
        var template = ChatProviderCatalog.Resolve(configured.TemplateId);
        var model = ChatProviderCatalog.ResolveModel(template.Id, configured.SelectedModelId);
        if (string.IsNullOrWhiteSpace(configured.Id))
        {
            configured.Id = Guid.NewGuid().ToString("N");
        }

        configured.TemplateId = template.Id;
        configured.ProtocolId = template.ProtocolId;
        configured.Name = template.Name;
        configured.BaseUrl = string.IsNullOrWhiteSpace(configured.BaseUrl)
            ? template.DefaultBaseUrl
            : IsLegacyProviderHost(configured.BaseUrl)
                ? template.DefaultBaseUrl
                : configured.BaseUrl.Trim();
        configured.SelectedModelId = model.Id;
        configured.ModelParameters = NormalizeModelParameterValues(template.Id, model.Id, configured.ModelParameters);
    }

    private static void EnsureActiveProvider(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ActiveConfiguredProviderId) && settings.ConfiguredProviders.Count > 0)
        {
            settings.ActiveConfiguredProviderId = settings.ConfiguredProviders[0].Id;
        }
        else if (settings.ConfiguredProviders.Count > 0 &&
                 settings.ConfiguredProviders.All(provider => provider.Id != settings.ActiveConfiguredProviderId))
        {
            settings.ActiveConfiguredProviderId = settings.ConfiguredProviders[0].Id;
        }
    }

    private static void DeduplicateConfiguredProviders(AppSettings settings)
    {
        if (settings.ConfiguredProviders.Count < 2)
        {
            return;
        }

        var activeId = settings.ActiveConfiguredProviderId;
        var uniqueProviders = settings.ConfiguredProviders
            .GroupBy(provider => $"{provider.TemplateId}|{provider.ApiKey}", StringComparer.Ordinal)
            .Select(group =>
                group.FirstOrDefault(provider => provider.Id == activeId) ??
                group.First())
            .ToList();

        if (uniqueProviders.Count == settings.ConfiguredProviders.Count)
        {
            return;
        }

        settings.ConfiguredProviders.Clear();
        settings.ConfiguredProviders.AddRange(uniqueProviders);
        if (settings.ConfiguredProviders.All(provider => provider.Id != activeId))
        {
            settings.ActiveConfiguredProviderId = settings.ConfiguredProviders.FirstOrDefault()?.Id ?? "";
        }
    }

    // Shared URL shape check used by Normalize to decide whether
    // a stored BaseUrl is worth keeping. Mirrors the equivalent
    // private helper in ProviderConfigurationValidator (the two
    // services intentionally keep the rules independent — the
    // validator's full check also runs on every UI save, so
    // Normalize's job is only to be a defensive boot-time guard).
    private static bool IsValidHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               !string.IsNullOrWhiteSpace(uri.Host);
    }

    // Hostnames that belonged to providers removed in the 1.0 Provider
    // prune. A 0.5 user upgrading with one of these stored in their
    // BaseUrl would otherwise hit `https://api.anthropic.com/v1/
    // chat/completions` (404 — no such endpoint on Anthropic) or
    // `https://token-plan-cn.xiaomimimo.com/v1/chat/completions` with
    // a model name that Xiaomi's gateway does not recognise. Both
    // surface as opaque auth / 404 errors to the user. Keep this list
    // tight: only the providers the catalog definitively retired.
    internal static readonly System.Collections.Generic.HashSet<string> LegacyProviderHosts =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            "api.anthropic.com",
            "api.deepseek.com",
            "token-plan-cn.xiaomimimo.com",
            "api.xiaomimimo.com",
            // 2026-08-04: api.minimax.io is the host that the
            // pre-1.0.0 catalog defaulted to. The live MiniMax
            // surface for M3 / M3-highspeed / M2.7 is now
            // api.minimax.chat; .io is a redirect / older
            // gateway that returns HTTP 401 ("invalid api key
            // (2049)") for keys minted on the current platform.
            // Auto-rewrite old .io entries to the catalog
            // default (.chat) so the next message goes to a
            // host that actually accepts the user's key. True
            // self-hosted users on a custom .io host are
            // unaffected because the comparison is on the host
            // segment, not the full URL — a "proxy.io" or
            // "minimax-proxy.example.io" stays put.
            "api.minimax.io"
        };

    private static bool IsLegacyProviderHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }
        return LegacyProviderHosts.Contains(uri.Host);
    }
}
