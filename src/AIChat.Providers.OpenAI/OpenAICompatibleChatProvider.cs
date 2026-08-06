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
        // Match by either product provider ID or lower-level protocol ID. The
        // protocol match is what lets MiniMax (the only ship target after
        // the 2026-08-02 catalog prune) reuse this adapter — MiniMax is
        // OpenAI-protocol, the Info.Id check covers it for settings that
        // carry the canonical "minimax" provider id, and the ProtocolId
        // check covers any legacy settings that pre-date the prune.
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
            // 2026-08-05: ask the platform to attach the
            // token-usage block to the final streaming
            // chunk. Without this flag the
            // OpenAI-compatible surface omits usage
            // entirely on streaming responses, and the
            // runner can't surface the cache hit rate
            // (or even the billed token count) in the
            // UI. MiniMax honors the standard
            // `stream_options.include_usage` shape —
            // verified on 2026-08-05 with a curl probe
            // (response included prompt_tokens,
            // completion_tokens, and
            // prompt_tokens_details.cached_tokens).
            ["stream_options"] = new Dictionary<string, object?>
            {
                ["include_usage"] = true
            },
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
                    parameters = ParseToolSchema(tool.ParametersJson)
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
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
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
            if (delta.IsCompleted)
            {
                foreach (var toolCall in FlushToolCalls(toolCallChunks))
                {
                    yield return new ChatDelta { ToolCalls = [toolCall] };
                }

                yield return StripInternalToolCallChunks(delta);
                yield break;
            }

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
        // Some OpenAI-compatible providers stream reasoning content
        // separately from the final answer (DeepSeek's thinking mode
        // was the original use case; MiniMax's interleaved thinking
        // uses the same field). The OpenAI protocol field name is
        // `reasoning_content`; we pass it back as a sibling of
        // `content` so the model sees its own reasoning in the
        // next turn. Without this the model loses its chain of
        // thought across multi-turn reasoning tasks.
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

    private static readonly JsonElement EmptyObjectSchema =
        JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\",\"properties\":{}}");

    private static JsonElement ParseToolSchema(string? json)
    {
        var element = ParseJsonSafe(json);
        return element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any()
            ? element
            : EmptyObjectSchema;
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
        // AppSettings.MaxOutputTokens is a real schema field, gets
        // clamped on every load by the inline normalize in
        // MainWindowViewModel ctor, and is honored by the Anthropic
        // provider. The OpenAI-
        // compatible path also takes max_tokens — every model that
        // speaks the /chat/completions schema understands it — but
        // the request payload was built from ChatRequest which
        // doesn't carry a max_tokens field, so the schema setting
        // was silently ignored for OpenAI users. Inject it here
        // instead, mirroring the Anthropic provider's direct
        // payload["max_tokens"] = settings.MaxOutputTokens.
        payload["max_tokens"] = settings.MaxOutputTokens;

        foreach (var parameter in settings.ModelParameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Value))
            {
                continue;
            }

            switch (parameter.Key)
            {
                // 2026-08-04: M3 native thinking-mode switch
                // (the knob the daily driver actually wants
                // when they say "思考模式的开关" — see
                // ChatProviderCatalog.MiniMaxM3Parameters).
                // Per the M3 README the values are
                // `enabled` / `adaptive` / `disabled`. The
                // M3 OpenAI-compatible path accepts them as a
                // top-level string field (not the Anthropic
                // {"type":"enabled"} object form). Catalog
                // dropdowns are the only source of these
                // values, so we don't need to parse defensively
                // here — a freeform text entry would round-trip
                // the literal user input, which is the desired
                // behavior for a power user typing a custom
                // M3.x / M4 value into the Settings modal.
                // Empty / whitespace falls through the early
                // `continue` above so we never send an empty
                // `thinking: ""` that would override the
                // platform default. M2.7 is unaffected — it
                // doesn't list this parameter in
                // MiniMaxM27Parameters, so the dropdown never
                // surfaces it for M2.x.
                case "minimax.thinking":
                    payload["thinking"] = parameter.Value;
                    break;
                // 2026-08-02: DeepSeek-specific parameter shaping
                // (`deepseek.thinking` / `deepseek.reasoning_effort`
                // / `deepseek.response_format`) is gone — the
                // DeepSeek provider was pruned from the catalog.
                // Old settings files that still carry these keys
                // fall through ProviderConfigurationValidator's
                // "unknown parameter" warning path, and this
                // switch silently skips them. The MiniMax shape
                // below is the only one that survives.
                case "minimax.reasoning_split" when bool.TryParse(parameter.Value, out var reasoningSplit):
                    payload["reasoning_split"] = reasoningSplit;
                    break;
                // 2026-08-04: top_p (nucleus sampling). MiniMax
                // honors the same parameter on the
                // /chat/completions surface as OpenAI. The UI
                // exposes it as a per-model dropdown (see
                // ChatProviderCatalog.MiniMaxM3Parameters) with
                // preset values 0.1 / 0.5 / 0.9 / 0.95 / 1.0;
                // any empty / non-numeric value falls through
                // and is ignored (the API default applies).
                // Mapped through double.TryParse with
                // InvariantCulture so a locale-formatted "0,5"
                // (German / French) doesn't sneak in — the
                // catalog emits a fixed "." decimal point.
                case "top_p" when double.TryParse(parameter.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var topP):
                    payload["top_p"] = topP;
                    break;
                // 2026-08-04: parallel_tool_calls. M3 supports
                // firing multiple tool calls in a single turn
                // (e.g. read 3 files in parallel) — disabling
                // it here forces single-flight and is the
                // escape hatch when a daily driver hits a
                // per-request parallel-call rate limit. Sent
                // as a boolean; the API ignores it on models
                // that don't honor it (M2.7), so the same
                // Settings file is safe across the model
                // dropdown even though only M3 reads the value.
                case "parallel_tool_calls" when bool.TryParse(parameter.Value, out var parallelToolCalls):
                    payload["parallel_tool_calls"] = parallelToolCalls;
                    break;
                // 2026-08-04: structured JSON output
                // (response_format). Standard
                // OpenAI-compatible shape: when the user picks
                // "json_object" from the Settings dropdown, we
                // inject {"type": "json_object"} into the
                // payload and the M3 model is forced to emit
                // valid JSON. The only value we accept is
                // "json_object" — anything else (the empty
                // default OR a freeform value) is treated as
                // "leave the platform default in place" and
                // nothing is sent. The OpenAI API itself
                // validates that the prompt contains the word
                // "json" in some form; if the user picks this
                // without adjusting their prompt, the API
                // returns 400 and the existing error pipeline
                // surfaces the message verbatim.
                case "response_format" when string.Equals(parameter.Value, "json_object", StringComparison.OrdinalIgnoreCase):
                    payload["response_format"] = new Dictionary<string, object?>
                    {
                        ["type"] = "json_object"
                    };
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
            content = ToOpenAIContent(message)
        };
    }

    private static object ToOpenAIContent(ChatMessage message)
    {
        if (message.ContentParts.Count == 0)
        {
            return message.Content;
        }

        var parts = new List<object>();
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            parts.Add(new { type = "text", text = message.Content });
        }

        foreach (var part in message.ContentParts)
        {
            if (string.Equals(part.Type, "text", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(part.Text))
            {
                parts.Add(new { type = "text", text = part.Text });
            }
            else if (string.Equals(part.Type, "image", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(part.DataBase64) &&
                     !string.IsNullOrWhiteSpace(part.MediaType))
            {
                parts.Add(new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = $"data:{part.MediaType};base64,{part.DataBase64}"
                    }
                });
            }
        }

        return parts.Count == 0 ? message.Content : parts;
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
            // 2026-08-05: even the [DONE] sentinel can be
            // preceded by a chunk that carries only the
            // `usage` block (no choices). If the previous
            // chunk's usage was already captured, the
            // IsCompleted marker here just terminates the
            // stream — no need to re-attach usage. The
            // current chunk itself doesn't carry a
            // usage block (just "[DONE]"), so the
            // TryReadUsageNoContent path below would
            // return null.
            yield return new ChatDelta { IsCompleted = true, RawJson = payload };
            yield break;
        }

        // 2026-08-05: usage is delivered in the final
        // chunk alongside an empty `choices` array.
        // Parse it before the content checks so a
        // usage-only chunk still yields a delta (with
        // IsCompleted=false but Usage populated). The
        // OpenAI protocol reuses the same `data: …`
        // envelope for the usage chunk; we just need
        // to read past the missing choices array.
        var usage = TryReadUsage(payload);

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
                ToolCalls = toolCalls,
                Usage = usage
            };
        }
        else if (toolCalls.Count > 0)
        {
            yield return new ChatDelta { RawJson = payload, ToolCalls = toolCalls, Usage = usage };
        }
        else if (usage is not null)
        {
            // The usage-only final chunk — carries no
            // content, no reasoning, no tool calls, just
            // the token tally + cache hit. The runner
            // reads Usage off this delta to populate the
            // activity-feed footer / status-bar cache
            // ring.
            yield return new ChatDelta { RawJson = payload, Usage = usage };
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

    // 2026-08-05: extract the platform's per-call
    // usage block from a streaming chunk. The shape
    // (when stream_options.include_usage is true):
    //   {
    //     "id": "…", "model": "…", "choices": [],
    //     "usage": {
    //       "prompt_tokens": 177,
    //       "completion_tokens": 5,
    //       "total_tokens": 182,
    //       "prompt_tokens_details": {
    //         "cached_tokens": 114
    //       }
    //     }
    //   }
    // The cached_tokens field is MiniMax-specific
    // (the M3 README mentions automatic prompt cache
    // at 1/5 input price; the field surfaces how much
    // of the prompt was served from cache). Other
    // OpenAI-compatible providers may omit
    // prompt_tokens_details entirely — the read is
    // defensive (TryGetProperty + default int 0). The
    // function returns null on the no-usage case so
    // the caller can distinguish "platform didn't
    // include usage" from "usage was 0" (e.g. a
    // zero-token ping).
    private static ChatUsage? TryReadUsage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var promptTokens = usage.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number
                ? p.GetInt32()
                : 0;
            var completionTokens = usage.TryGetProperty("completion_tokens", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetInt32()
                : 0;
            var cachedTokens = 0;
            if (usage.TryGetProperty("prompt_tokens_details", out var details) &&
                details.ValueKind == JsonValueKind.Object &&
                details.TryGetProperty("cached_tokens", out var cached) &&
                cached.ValueKind == JsonValueKind.Number)
            {
                cachedTokens = cached.GetInt32();
            }

            return new ChatUsage
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                CachedTokens = cachedTokens
            };
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
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
