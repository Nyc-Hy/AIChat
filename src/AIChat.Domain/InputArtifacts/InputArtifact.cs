namespace AIChat.Domain.Artifacts;

public sealed class InputArtifact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string MessageId { get; set; } = "";
    public InputArtifactKind Kind { get; set; } = InputArtifactKind.Unknown;
    public string FileName { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string RawText { get; set; } = "";
    public string Summary { get; set; } = "";
    public string OcrText { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string RefId => $"input-artifact:{Id}";
}
