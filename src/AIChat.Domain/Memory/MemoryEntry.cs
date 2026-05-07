namespace AIChat.Domain.Memory;

public sealed class MemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public MemoryCategory Category { get; set; } = MemoryCategory.Project;
    public string Content { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
