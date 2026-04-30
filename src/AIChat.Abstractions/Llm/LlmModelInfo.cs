namespace AIChat.Abstractions.Llm;

// Static metadata for a selectable model. The context limit feeds the usage ring
// and the future Agent context budget.
public sealed class LlmModelInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public int ContextLimit { get; init; }
    public string CapabilityLabel { get; init; } = "";
    public LlmModelCapabilities Capabilities { get; init; } = new();
    public IReadOnlyList<LlmModelParameterInfo> Parameters { get; init; } = [];

    public override string ToString()
    {
        return DisplayName;
    }
}
