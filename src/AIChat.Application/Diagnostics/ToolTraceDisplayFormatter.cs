using System.Text.Encodings.Web;
using System.Text.Json;
using AIChat.Application.Security;

namespace AIChat.Application.Diagnostics;

public static class ToolTraceDisplayFormatter
{
    private static readonly JsonSerializerOptions JsonDisplayOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string CompactJson(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = value.Trim();
        try
        {
            using var document = JsonDocument.Parse(normalized);
            normalized = JsonSerializer.Serialize(document.RootElement, JsonDisplayOptions);
        }
        catch (JsonException)
        {
            normalized = normalized.ReplaceLineEndings(" ");
        }

        return Truncate(SensitiveDataRedactor.RedactText(normalized), maxLength);
    }

    public static string TryReadString(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return "";
            }

            var value = property.ValueKind switch
            {
                JsonValueKind.String => property.GetString() ?? "",
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.ToString(),
                _ => JsonSerializer.Serialize(property, JsonDisplayOptions)
            };
            return SensitiveDataRedactor.RedactText(value);
        }
        catch (JsonException)
        {
            return "";
        }
    }

    public static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }
}
