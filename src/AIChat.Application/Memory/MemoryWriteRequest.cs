using AIChat.Domain.Memory;

namespace AIChat.Application.Memory;

public sealed class MemoryWriteRequest
{
    public required string ProjectId { get; init; }
    public required MemoryCategory Category { get; init; }
    public required string Content { get; init; }
    public string Source { get; init; } = "";
    public bool UserConfirmed { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
