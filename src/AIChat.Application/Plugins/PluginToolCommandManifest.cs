using System.Text.Json.Serialization;

namespace AIChat.Application.Plugins;

public sealed class PluginToolCommandManifest
{
    [JsonPropertyName("executable")]
    public string Executable { get; set; } = "";

    [JsonPropertyName("arguments")]
    public List<string> Arguments { get; set; } = [];

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = "";

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("maxOutputChars")]
    public int MaxOutputChars { get; set; } = 12000;
}
