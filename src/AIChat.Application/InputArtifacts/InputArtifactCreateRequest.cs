namespace AIChat.Application.Artifacts;

public sealed class InputArtifactCreateRequest
{
    public string ProjectId { get; init; } = "";
    public string ConversationId { get; init; } = "";
    public string MessageId { get; init; } = "";
    public string FileName { get; init; } = "";
    public string MimeType { get; init; } = "";
    public string ContentText { get; init; } = "";
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
