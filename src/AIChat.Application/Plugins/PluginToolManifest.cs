using System.Text.Json.Serialization;

namespace AIChat.Application.Plugins;

public sealed class PluginToolManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("risk")]
    public string Risk { get; set; } = "shell";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "插件";

    [JsonPropertyName("groupLabel")]
    public string GroupLabel { get; set; } = "插件工具";

    [JsonPropertyName("parametersJson")]
    [JsonConverter(typeof(FlexibleJsonStringConverter))]
    public string ParametersJson { get; set; } = """
    {
      "type": "object",
      "properties": {}
    }
    """;

    [JsonPropertyName("command")]
    public PluginToolCommandManifest Command { get; set; } = new();
}
