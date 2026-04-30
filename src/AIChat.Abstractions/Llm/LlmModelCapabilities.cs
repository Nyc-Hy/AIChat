namespace AIChat.Abstractions.Llm;

public sealed class LlmModelCapabilities
{
    public bool SupportsTools { get; init; }
    public bool SupportsThinking { get; init; }
    public bool SupportsJsonOutput { get; init; }
    public bool SupportsInterleavedThinking { get; init; }
    public bool SupportsPrefixCompletion { get; init; }
}
