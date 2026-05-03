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

        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["max_tokens"] = settings.MaxOutputTokens,
            ["temperature"] = request.Temperature,
            ["stream"] = true,
            ["system"] = string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
            ["messages"] = ToAnthropicMessages(
                request.Messages.Where(m => m.Role != ChatRole.System).ToList())
        };

        if (request.Tools.Count > 0)
        {
            payload["tools"] = request.Tools.Select(tool => new
            {
                name = tool.Name,
                description = tool.Description,
                input_schema = ParseToolSchema(tool.ParametersJson)
            }).ToList();
        }

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
            yield return new ChatDelta
            {
                Content = $"Anthropic 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\n\n{error}",
                RawJson = error,
                HttpStatusCode = (int)response.StatusCode
            };
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        // Track in-flight tool_use blocks across streaming events.
        var pendingToolCalls = new Dictionary<int, ChatToolCall>();
        var pendingToolInputs = new Dictionary<int, StringBuilder>();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();

            // Try to handle tool_use lifecycle events.
            if (TryHandleToolEvent(data, pendingToolCalls, pendingToolInputs, out var toolDelta))
            {
                if (toolDelta is not null)
                {
                    yield return toolDelta;
                }

                continue;
            }

            // Surface text deltas.
            var content = TryReadDeltaText(data);
            if (!string.IsNullOrEmpty(content))
            {
                yield return new ChatDelta { Content = content, RawJson = data };
            }
        }
    }

    internal static List<object> ToAnthropicMessages(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<object>();

        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];

            // Consecutive tool results are merged into a single user message
            // with multiple tool_result content blocks.
            if (message.Role == ChatRole.Tool)
            {
                var toolBlocks = new List<object>();
                toolBlocks.Add(MakeToolResultBlock(message));

                while (i + 1 < messages.Count && messages[i + 1].Role == ChatRole.Tool)
                {
                    i++;
                    toolBlocks.Add(MakeToolResultBlock(messages[i]));
                }

                result.Add(new { role = "user", content = toolBlocks });
                continue;
            }

            // Assistant messages with tool calls use content blocks.
            if (message.Role == ChatRole.Assistant && message.ToolCalls.Count > 0)
            {
                var blocks = new List<object>();

                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    blocks.Add(new { type = "text", text = message.Content });
                }

                foreach (var call in message.ToolCalls)
                {
                    blocks.Add(new
                    {
                        type = "tool_use",
                        id = call.Id,
                        name = call.Name,
                        input = ParseJsonSafe(call.ArgumentsJson)
                    });
                }

                result.Add(new { role = "assistant", content = blocks });
                continue;
            }

            // Plain user/assistant messages.
            result.Add(new
            {
                role = message.Role == ChatRole.Assistant ? "assistant" : "user",
                content = message.Content
            });
        }

        return result;
    }

    private static object MakeToolResultBlock(ChatMessage message)
    {
        return new
        {
            type = "tool_result",
            tool_use_id = message.ToolCallId,
            content = message.Content
        };
    }

    private static readonly JsonElement EmptyObjectSchema =
        JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\",\"properties\":{}}");

    private static JsonElement ParseToolSchema(string? json)
    {
        var element = ParseJsonSafe(json);
        return element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any()
            ? element
            : EmptyObjectSchema;
    }

    public static JsonElement ParseJsonSafe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return JsonSerializer.Deserialize<JsonElement>("{}");
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            return JsonSerializer.Deserialize<JsonElement>("{}");
        }
    }

    internal static bool TryHandleToolEvent(
        string json,
        Dictionary<int, ChatToolCall> pendingToolCalls,
        Dictionary<int, StringBuilder> pendingToolInputs,
        out ChatDelta? delta)
    {
        delta = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type))
            {
                return false;
            }

            var eventType = type.GetString();

            // content_block_start with type "tool_use" begins a new tool call.
            if (eventType == "content_block_start" &&
                root.TryGetProperty("index", out var startIdx) &&
                root.TryGetProperty("content_block", out var block) &&
                block.TryGetProperty("type", out var blockType) &&
                blockType.GetString() == "tool_use")
            {
                var index = startIdx.GetInt32();
                var id = block.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : $"tool-{index}";
                var name = block.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                pendingToolCalls[index] = new ChatToolCall { Id = id, Name = name, Index = index };
                pendingToolInputs[index] = new StringBuilder();
                return true;
            }

            // content_block_delta with type "input_json_delta" carries partial JSON.
            if (eventType == "content_block_delta" &&
                root.TryGetProperty("index", out var deltaIdx) &&
                root.TryGetProperty("delta", out var deltaObj) &&
                deltaObj.TryGetProperty("type", out var deltaType) &&
                deltaType.GetString() == "input_json_delta" &&
                deltaObj.TryGetProperty("partial_json", out var partialJson))
            {
                var index = deltaIdx.GetInt32();
                if (pendingToolInputs.TryGetValue(index, out var sb))
                {
                    sb.Append(partialJson.GetString() ?? "");
                }

                return true;
            }

            // content_block_stop finalizes a tool call.
            if (eventType == "content_block_stop" &&
                root.TryGetProperty("index", out var stopIdx))
            {
                var index = stopIdx.GetInt32();
                if (pendingToolCalls.TryGetValue(index, out var toolCall))
                {
                    var inputJson = pendingToolInputs.TryGetValue(index, out var sb) ? sb.ToString() : "";
                    toolCall.ArgumentsJson = string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson;
                    delta = new ChatDelta
                    {
                        ToolCalls = [toolCall],
                        RawJson = json
                    };
                    pendingToolCalls.Remove(index);
                    pendingToolInputs.Remove(index);
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
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
