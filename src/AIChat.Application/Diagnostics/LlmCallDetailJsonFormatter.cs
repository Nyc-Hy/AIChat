using System.Text.Encodings.Web;
using System.Text.Json;
using AIChat.Application.Security;

namespace AIChat.Application.Diagnostics;

public static class LlmCallDetailJsonFormatter
{
    public const int MaxRawEventsToDisplay = 120;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string BuildRequestSummary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var messageCount = root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array
                ? messages.GetArrayLength()
                : 0;
            var toolCount = root.TryGetProperty("enabledTools", out var tools) && tools.ValueKind == JsonValueKind.Array
                ? tools.GetArrayLength()
                : 0;
            var imageCount = CountContentParts(root, "image");
            var textPartCount = CountContentParts(root, "text");
            var parts = new List<string>();
            if (messageCount > 0)
            {
                parts.Add($"{messageCount} 条消息");
            }

            parts.Add($"{toolCount} 个工具");
            if (imageCount > 0)
            {
                parts.Add($"{imageCount} 张图片");
            }

            if (textPartCount > 0)
            {
                parts.Add($"{textPartCount} 个文本片段");
            }

            return string.Join(" · ", parts);
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public static string NormalizeJsonText(string json, bool includeRawEvents)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        var safeJson = SensitiveDataRedactor.RedactText(json);
        try
        {
            using var document = JsonDocument.Parse(safeJson);
            var normalized = NormalizeElement(document.RootElement, includeRawEvents);
            return JsonSerializer.Serialize(normalized, JsonOptions);
        }
        catch (JsonException)
        {
            return safeJson;
        }
    }

    private static int CountContentParts(JsonElement root, string type)
    {
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var count = 0;
        foreach (var message in messages.EnumerateArray())
        {
            if (!message.TryGetProperty("contentParts", out var contentParts) ||
                contentParts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            count += contentParts.EnumerateArray().Count(part =>
                part.TryGetProperty("type", out var partType) &&
                string.Equals(partType.GetString(), type, StringComparison.OrdinalIgnoreCase));
        }

        return count;
    }

    private static object? NormalizeElement(JsonElement element, bool includeRawEvents)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => NormalizeObject(element, includeRawEvents),
            JsonValueKind.Array => element.EnumerateArray().Select(item => NormalizeElement(item, includeRawEvents)).ToList(),
            JsonValueKind.String => NormalizeString(element.GetString() ?? ""),
            JsonValueKind.Number => element.TryGetInt64(out var longValue)
                ? longValue
                : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static Dictionary<string, object?> NormalizeObject(JsonElement element, bool includeRawEvents)
    {
        var result = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name == "rawEvents" && property.Value.ValueKind == JsonValueKind.Array)
            {
                result[includeRawEvents ? "rawEvents" : "rawEventsSummary"] = includeRawEvents
                    ? NormalizeRawEvents(property.Value)
                    : CreateRawEventsSummary(property.Value);
                continue;
            }

            result[property.Name] = NormalizeElement(property.Value, includeRawEvents);
        }

        return result;
    }

    private static IReadOnlyList<object?> NormalizeRawEvents(JsonElement rawEvents)
    {
        var items = rawEvents.EnumerateArray().ToList();
        var normalized = items
            .Take(MaxRawEventsToDisplay)
            .Select(rawEvent => rawEvent.ValueKind == JsonValueKind.String
                ? NormalizeString(rawEvent.GetString() ?? "")
                : NormalizeElement(rawEvent, includeRawEvents: true))
            .ToList();

        if (items.Count > MaxRawEventsToDisplay)
        {
            normalized.Add(new Dictionary<string, object?>
            {
                ["notice"] = "rawEvents 太多，界面仅展示前 120 条以避免卡顿。",
                ["totalRawEvents"] = items.Count,
                ["hiddenRawEvents"] = items.Count - MaxRawEventsToDisplay
            });
        }

        return normalized;
    }

    private static object NormalizeString(string value)
    {
        if (value == "[DONE]")
        {
            return value;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return NormalizeElement(document.RootElement, includeRawEvents: true) ?? "";
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static Dictionary<string, object?> CreateRawEventsSummary(JsonElement rawEvents)
    {
        var items = rawEvents.EnumerateArray().ToList();
        return new Dictionary<string, object?>
        {
            ["total"] = items.Count,
            ["preview"] = "原始流式事件默认隐藏，勾选“展开原始事件”后查看前 120 条。"
        };
    }
}
