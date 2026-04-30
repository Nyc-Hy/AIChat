namespace AIChat.Application.Tools;

public sealed class AgentToolPreview
{
    public required string ToolName { get; init; }
    public AgentToolRisk Risk { get; init; }
    public string Summary { get; init; } = "";
    public string PreviewText { get; init; } = "";
    public string DiffText { get; init; } = "";
}
