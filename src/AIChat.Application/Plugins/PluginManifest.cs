using System.Text.Json.Serialization;

namespace AIChat.Application.Plugins;

public sealed class PluginManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("tools")]
    public List<PluginToolManifest> Tools { get; set; } = [];

    [JsonIgnore]
    public string DirectoryPath { get; set; } = "";
}
