using System.Net.Http.Headers;
using AIChat.Abstractions.Llm;

namespace AIChat.Application.Llm.Routing;

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
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsEndpoint(template, provider.BaseUrl));
            ApplyAuthHeaders(request, template, provider.ApiKey);
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

    private static string BuildModelsEndpoint(LlmProviderInfo template, string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return string.Equals(template.ProtocolId, "anthropic", StringComparison.OrdinalIgnoreCase)
            ? $"{trimmed}/v1/models"
            : $"{trimmed}/models";
    }

    private static void ApplyAuthHeaders(HttpRequestMessage request, LlmProviderInfo template, string apiKey)
    {
        if (string.Equals(template.ProtocolId, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }
}
