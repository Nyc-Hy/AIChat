using System.Text.Json.Serialization;

namespace AIChat.Application.Plugins;

public sealed class PluginSkillManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "SKILL.md";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}
