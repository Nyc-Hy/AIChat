using AIChat.Domain.Chat;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AIChat.App.ViewModels;

// View model for the call-detail inspector. It formats and optionally truncates
// raw provider events so debugging data does not freeze the UI.
public sealed class LlmCallDetailViewModel : ObservableObject
{
    private const int MaxRawEventsToDisplay = 120;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public LlmCallDetailViewModel(LlmCallDetail detail)
    {
        Detail = detail;
    }

    public LlmCallDetail Detail { get; }
    public string Title => $"{Detail.CreatedAt.ToLocalTime():MM/dd HH:mm:ss} · {Detail.Model}";
    public string Subtitle => $"{Detail.ProviderName} · {Detail.Status}";
    public string RequestJson => NormalizeJsonText(Detail.RequestJson, includeRawEvents: true);
    public string ResponseJson => NormalizeJsonText(Detail.ResponseJson, includeRawEvents: false);
    public string ResponseJsonWithRawEvents => NormalizeJsonText(Detail.ResponseJson, includeRawEvents: true);

    private static string NormalizeJsonText(string json, bool includeRawEvents)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var normalized = NormalizeElement(document.RootElement, includeRawEvents);
            return JsonSerializer.Serialize(normalized, JsonOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static object? NormalizeElement(JsonElement element, bool includeRawEvents)
    {
        // Convert JsonElement into ordinary .NET objects so JsonSerializer can
        // pretty-print a normalized, readable tree.
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
                // Raw streaming events are useful but noisy, so the default view
                // shows a summary and lets the user opt into expanded events.
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
            // Keep large streaming sessions inspectable without rendering every event.
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
