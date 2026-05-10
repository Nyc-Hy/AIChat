namespace AIChat.Abstractions.Llm;

// User-specific provider configuration derived from a provider template plus an
// API key and selected model.
public sealed class ConfiguredLlmProvider
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TemplateId { get; set; } = "";
    public string ProtocolId { get; set; } = "openai";
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string SelectedModelId { get; set; } = "";
    public bool SupportsVisionOverride { get; set; }
    public Dictionary<string, string> ModelParameters { get; set; } = [];
    // Shows enough of the key to identify an entry without exposing the secret.
    public string DisplayName => string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Length < 8
        ? Name
        : $"{Name} · {ApiKey[^4..]}";

    public override string ToString()
    {
        return DisplayName;
    }
}
