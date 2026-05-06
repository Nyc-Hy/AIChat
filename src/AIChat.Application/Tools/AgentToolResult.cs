namespace AIChat.Application.Tools;

public sealed class AgentToolResult
{
    public required string ToolName { get; init; }
    public required string Content { get; init; }
    public bool IsError { get; init; }
    public string ModelContent { get; init; } = "";
    public bool WasSummarized { get; init; }
    public string ArtifactKind { get; init; } = "";
    public string Summary { get; init; } = "";

    public string ContentForModel => string.IsNullOrWhiteSpace(ModelContent) ? Content : ModelContent;
}
