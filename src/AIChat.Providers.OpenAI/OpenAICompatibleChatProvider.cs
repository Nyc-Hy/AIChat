using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;

namespace AIChat.Providers.OpenAI;

// Adapter for OpenAI-compatible chat completion APIs. It hides HTTP, payload
// shape, and server-sent-event parsing behind the common IChatProvider contract.
public sealed class OpenAICompatibleChatProvider : IChatProvider
{
    private readonly HttpClient _httpClient;

    public OpenAICompatibleChatProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public LlmProviderInfo Info { get; } = new()
    {
        Id = "openai",
        ProtocolId = "openai",
        Name = "OpenAI Compatible",
        DefaultBaseUrl = "https://api.openai.com/v1",
        DefaultModel = "gpt-4.1-mini",
        DefaultContextLimit = 128_000
    };

    public bool CanHandle(AppSettings settings)
    {
        // Match by either product provider ID or lower-level protocol ID. This is
        // why TokenPlan MIMO can reuse this adapter.
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
            yield return new ChatDelta
            {
                Content = $"还没有配置 {settings.ProviderName} API Key。请打开设置添加模型提供商。"
            };
            yield break;
        }

        var endpoint = $"{settings.BaseUrl.TrimEnd('/')}/chat/completions";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        // Streaming responses are delivered as server-sent events: data: {...}
        // lines separated by blank lines.
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Keep the request payload provider-neutral until this boundary, then map
        // app settings into the OpenAI-compatible field names.
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["temperature"] = request.Temperature,
            ["stream"] = true,
            ["messages"] = request.Messages.Select(ToApiMessage).ToList()
        };

        if (request.Tools.Count > 0)
        {
            payload["tools"] = request.Tools.Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = ParseJsonSafe(tool.ParametersJson)
                }
            }).ToList();
            payload["tool_choice"] = "auto";
        }

        ApplyProviderSpecificParameters(payload, settings);

        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload),
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
                Content = $"LLM 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\n\n{error}",
                RawJson = error,
                HttpStatusCode = (int)response.StatusCode
            };
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var eventData = new StringBuilder();
        var toolCallChunks = new Dictionary<int, ToolCallChunk>();

        // SSE frames may span multiple lines. Accumulate data: lines until a
        // blank line, then parse the complete event.
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                foreach (var delta in FlushEventData(eventData))
                {
                    AccumulateToolCalls(delta, toolCallChunks);
                    yield return StripInternalToolCallChunks(delta);
                    if (delta.IsCompleted)
                    {
                        foreach (var toolCall in FlushToolCalls(toolCallChunks))
                        {
                            yield return new ChatDelta { ToolCalls = [toolCall] };
                        }

                        yield break;
                    }
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                eventData.AppendLine(line["data:".Length..].Trim());
            }
        }

        foreach (var delta in FlushEventData(eventData))
        {
            AccumulateToolCalls(delta, toolCallChunks);
            yield return StripInternalToolCallChunks(delta);
        }

        foreach (var toolCall in FlushToolCalls(toolCallChunks))
        {
            yield return new ChatDelta { ToolCalls = [toolCall] };
        }
    }

    private static object BuildAssistantMessage(
        ChatMessage message,
        string? content,
        string? reasoningContent,
        IReadOnlyList<ChatToolCall>? toolCalls)
    {
        // DeepSeek thinking mode requires reasoning_content to be passed back.
        if (!string.IsNullOrWhiteSpace(reasoningContent))
        {
            var result = new Dictionary<string, object?>
            {
                ["role"] = "assistant",
                ["content"] = string.IsNullOrWhiteSpace(content) ? null : content,
                ["reasoning_content"] = reasoningContent
            };
            if (toolCalls is { Count: > 0 })
            {
                result["tool_calls"] = toolCalls.Select(call => new
                {
                    id = call.Id,
                    type = "function",
                    function = new
                    {
                        name = call.Name,
                        arguments = call.ArgumentsJson
                    }
                }).ToList();
            }

            return result;
        }

        if (toolCalls is { Count: > 0 })
        {
            return new
            {
                role = "assistant",
                content = string.IsNullOrWhiteSpace(content) ? null : content,
                tool_calls = toolCalls.Select(call => new
                {
                    id = call.Id,
                    type = "function",
                    function = new
                    {
                        name = call.Name,
                        arguments = call.ArgumentsJson
                    }
                }).ToList()
            };
        }

        return new
        {
            role = "assistant",
            content = string.IsNullOrWhiteSpace(content) ? null : content
        };
    }

    private static JsonElement ParseJsonSafe(string? json)
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

    private static string ToApiRole(ChatRole role) => role switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Tool => "tool",
        _ => "assistant"
    };

    private static void ApplyProviderSpecificParameters(Dictionary<string, object?> payload, AppSettings settings)
    {
        foreach (var parameter in settings.ModelParameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Value))
            {
                continue;
            }

            switch (parameter.Key)
            {
                case "deepseek.thinking":
                    payload["thinking"] = new { type = parameter.Value };
                    break;
                case "deepseek.reasoning_effort":
                    payload["reasoning_effort"] = parameter.Value;
                    break;
                case "deepseek.response_format" when parameter.Value == "json_object":
                    payload["response_format"] = new { type = "json_object" };
                    break;
                case "minimax.reasoning_split" when bool.TryParse(parameter.Value, out var reasoningSplit):
                    payload["reasoning_split"] = reasoningSplit;
                    break;
            }
        }
    }

    private static object ToApiMessage(ChatMessage message)
    {
        if (message.Role == ChatRole.Tool)
        {
            return new
            {
                role = "tool",
                tool_call_id = message.ToolCallId,
                content = message.Content
            };
        }

        if (message.Role == ChatRole.Assistant && message.ToolCalls.Count > 0)
        {
            return BuildAssistantMessage(message, message.Content, message.ReasoningContent,
                message.ToolCalls);
        }

        if (message.Role == ChatRole.Assistant)
        {
            return BuildAssistantMessage(message, message.Content, message.ReasoningContent, null);
        }

        return new
        {
            role = ToApiRole(message.Role),
            content = message.Content
        };
    }

    private static IEnumerable<ChatDelta> FlushEventData(StringBuilder eventData)
    {
        if (eventData.Length == 0)
        {
            yield break;
        }

        var payload = eventData.ToString().Trim();
        eventData.Clear();
        if (string.IsNullOrWhiteSpace(payload))
        {
            yield break;
        }

        if (payload == "[DONE]")
        {
            // Convert protocol-specific completion into the common stream signal.
            yield return new ChatDelta { IsCompleted = true, RawJson = payload };
            yield break;
        }

        var content = TryReadDeltaContent(payload);
        var reasoningContent = TryReadReasoningContent(payload);
        var toolCalls = TryReadToolCallDeltas(payload);
        if (!string.IsNullOrEmpty(content) || !string.IsNullOrEmpty(reasoningContent))
        {
            yield return new ChatDelta
            {
                Content = content,
                ReasoningContent = reasoningContent,
                RawJson = payload,
                ToolCalls = toolCalls
            };
        }
        else if (toolCalls.Count > 0)
        {
            yield return new ChatDelta { RawJson = payload, ToolCalls = toolCalls };
        }
    }

    private static string TryReadDeltaContent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? "";
                }

                if (choice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var messageContent))
                {
                    return messageContent.GetString() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            return "";
        }
        catch (Exception)
        {
            return "";
        }

        return "";
    }

    private static string TryReadReasoningContent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array)
            {
                return "";
            }

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("reasoning_content", out var reasoningContent))
                {
                    return reasoningContent.GetString() ?? "";
                }
            }
        }
        catch (JsonException)
        {
            return "";
        }
        catch (Exception)
        {
            return "";
        }

        return "";
    }

    private static IReadOnlyList<ChatToolCall> TryReadToolCallDeltas(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var calls = new List<ChatToolCall>();
            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("delta", out var delta) ||
                    !delta.TryGetProperty("tool_calls", out var toolCalls) ||
                    toolCalls.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    var id = toolCall.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
                    var name = "";
                    var arguments = "";
                    if (toolCall.TryGetProperty("function", out var function))
                    {
                        name = function.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                        arguments = function.TryGetProperty("arguments", out var argsElement) ? argsElement.GetString() ?? "" : "";
                    }

                    var index = toolCall.TryGetProperty("index", out var indexElement) && indexElement.TryGetInt32(out var value)
                        ? value
                        : calls.Count;
                    calls.Add(new ChatToolCall
                    {
                        Id = string.IsNullOrWhiteSpace(id) ? $"tool-{index}" : id,
                        Index = index,
                        Name = name,
                        ArgumentsJson = arguments
                    });
                }
            }

            return calls;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AccumulateToolCalls(ChatDelta delta, Dictionary<int, ToolCallChunk> chunks)
    {
        foreach (var call in delta.ToolCalls)
        {
            var index = call.Index;
            if (!chunks.TryGetValue(index, out var chunk))
            {
                chunk = new ToolCallChunk();
                chunks[index] = chunk;
            }

            if (!string.IsNullOrWhiteSpace(call.Id) && !call.Id.StartsWith("tool-", StringComparison.Ordinal))
            {
                chunk.Id = call.Id;
            }

            if (!string.IsNullOrWhiteSpace(call.Name))
            {
                chunk.Name.Append(call.Name);
            }

            if (!string.IsNullOrEmpty(call.ArgumentsJson))
            {
                chunk.Arguments.Append(call.ArgumentsJson);
            }
        }
    }

    private static IEnumerable<ChatToolCall> FlushToolCalls(Dictionary<int, ToolCallChunk> chunks)
    {
        foreach (var entry in chunks.OrderBy(item => item.Key))
        {
            var name = entry.Value.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            yield return new ChatToolCall
            {
                Id = string.IsNullOrWhiteSpace(entry.Value.Id) ? $"tool-{entry.Key}" : entry.Value.Id,
                Index = entry.Key,
                Name = name,
                ArgumentsJson = entry.Value.Arguments.Length == 0 ? "{}" : entry.Value.Arguments.ToString()
            };
        }
    }

    private static ChatDelta StripInternalToolCallChunks(ChatDelta delta)
    {
        return delta.ToolCalls.Count == 0
            ? delta
            : new ChatDelta
            {
                Content = delta.Content,
                RawJson = delta.RawJson,
                IsCompleted = delta.IsCompleted
            };
    }

    private sealed class ToolCallChunk
    {
        public string Id { get; set; } = "";
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();
    }
}
