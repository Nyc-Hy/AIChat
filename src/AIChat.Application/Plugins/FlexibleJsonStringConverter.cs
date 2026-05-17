using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIChat.Application.Plugins;

internal sealed class FlexibleJsonStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.ValueKind == JsonValueKind.String
            ? document.RootElement.GetString() ?? ""
            : document.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            writer.WriteStringValue(value);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            document.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            writer.WriteStringValue(value);
        }
    }
}
