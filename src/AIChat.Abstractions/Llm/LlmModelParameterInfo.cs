namespace AIChat.Abstractions.Llm;

// Model-specific request parameter that should only appear when the selected
// model/provider explicitly supports it.
public sealed class LlmModelParameterInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public string DefaultValue { get; init; } = "";
    public IReadOnlyList<LlmParameterOption> Options { get; init; } = [];
}
