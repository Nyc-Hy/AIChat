namespace AIChat.Domain.Context;

public sealed class PinnedContextItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public string Path { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
