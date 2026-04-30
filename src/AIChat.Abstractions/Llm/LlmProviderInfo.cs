namespace AIChat.Abstractions.Llm;

// Static provider template: protocol, default endpoint, default model, and the
// models users can select from in Settings.
public sealed class LlmProviderInfo
{
    public required string Id { get; init; }
    public required string ProtocolId { get; init; }
    public required string Name { get; init; }
    public required string DefaultBaseUrl { get; init; }
    public required string DefaultModel { get; init; }
    public int DefaultContextLimit { get; init; }
    public IReadOnlyList<LlmModelInfo> Models { get; init; } = [];

    public override string ToString()
    {
        return Name;
    }
}
