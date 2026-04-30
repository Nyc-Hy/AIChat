using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;

namespace AIChat.Providers.Anthropic;

// Adapter for Anthropic's Messages API. It demonstrates the same provider
// contract with a different request shape and streaming event format.
public sealed class AnthropicChatProvider : IChatProvider
{
    private readonly HttpClient _httpClient;

    public AnthropicChatProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public LlmProviderInfo Info { get; } = new()
    {
        Id = "anthropic",
        ProtocolId = "anthropic",
        Name = "Anthropic",
        DefaultBaseUrl = "https://api.anthropic.com",
        DefaultModel = "claude-3-5-sonnet-latest",
        DefaultContextLimit = 200_000
    };

    public bool CanHandle(AppSettings settings)
    {
        // Anthropic has its own protocol, so it should only handle matching
        // provider/protocol settings.
        return string.Equals(settings.ProviderId, Info.Id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(settings.ProtocolId, Info.ProtocolId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(settings.ProviderName, Info.Name, StringComparison.OrdinalIgnoreCase);
    }

    public async IAsyncEnumerable<ChatDelta> SendAsync(
        ChatRequest request,
        AppSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            yield return new ChatDelta { Content = "还没有配置 Anthropic API Key。请打开设置填写 API Key、Base URL 和模型名。" };
            yield break;
        }

        var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/v1/messages";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Add("x-api-key", settings.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Headers.Add("anthropic-beta", "messages-2023-12-15");

        // Anthropic separates the system prompt from normal user/assistant
        // messages, unlike OpenAI-compatible APIs.
        var systemPrompt = string.Join(
            "\n\n",
            request.Messages
                .Where(message => message.Role == ChatRole.System)
                .Select(message => message.Content)
                .Where(content => !string.IsNullOrWhiteSpace(content)));

        var payload = new
        {
            model = request.Model,
            max_tokens = 4096,
            temperature = request.Temperature,
            stream = true,
            system = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
            messages = request.Messages
                // System messages have already been folded into the system field.
                .Where(message => message.Role != ChatRole.System)
                .Select(message => new
                {
                    role = message.Role == ChatRole.Assistant ? "assistant" : "user",
                    content = message.Content
                })
        };

        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            yield return new ChatDelta { Content = $"Anthropic 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\n\n{error}", RawJson = error };
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            // We only surface text deltas. Other Anthropic event types are ignored
            // for now, but RawJson is preserved when text is emitted.
            var content = TryReadDeltaText(data);
            if (!string.IsNullOrEmpty(content))
            {
                yield return new ChatDelta { Content = content, RawJson = data };
            }
        }
    }

    private static string TryReadDeltaText(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            // Text arrives in content_block_delta events under delta.text.
            if (!root.TryGetProperty("type", out var type) ||
                type.GetString() != "content_block_delta" ||
                !root.TryGetProperty("delta", out var delta) ||
                !delta.TryGetProperty("text", out var text))
            {
                return "";
            }

            return text.GetString() ?? "";
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
