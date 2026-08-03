using AIChat.Abstractions.Llm;

namespace AIChat.Application.Llm.Routing;

// Sends a "GET /models" probe against the configured provider's
// BaseUrl to confirm the API key + endpoint shape are wired up
// correctly. 2026-08-02: collapsed to OpenAI-compatible auth —
// the catalog is MiniMax-only and MiniMax uses the OpenAI
// protocol, so the Anthropic branch (x-api-key + anthropic-version
// headers, /v1/models endpoint shape) that used to live here
// is dead. Removing it keeps the tester honest: there's no
// branch that claims to test a protocol the catalog no longer
// exposes.
public sealed class ProviderConnectionTester
{
    private readonly HttpClient _httpClient;

    public ProviderConnectionTester(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<ProviderConnectionTestResult> TestAsync(
        ConfiguredLlmProvider provider,
        CancellationToken cancellationToken = default)
    {
        var validation = ProviderConfigurationValidator.ValidateConfiguredProvider(provider);
        if (!validation.IsValid)
        {
            return ProviderConnectionTestResult.Failure(new ProviderErrorInfo(
                ProviderErrorKind.InvalidConfiguration,
                "模型配置无效",
                validation.Summary));
        }

        var template = ChatProviderCatalog.Resolve(provider.TemplateId);
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{provider.BaseUrl.TrimEnd('/')}/models");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", provider.ApiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return ProviderConnectionTestResult.Success($"{template.Name} 连接测试通过。");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ProviderConnectionTestResult.Failure(
                ProviderErrorClassifier.FromHttp((int)response.StatusCode, template.Name, body));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return ProviderConnectionTestResult.Failure(ProviderErrorClassifier.FromException(ex, template.Name));
        }
    }
}
