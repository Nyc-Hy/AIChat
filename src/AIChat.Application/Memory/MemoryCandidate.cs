using AIChat.Domain.Memory;

namespace AIChat.Application.Memory;

public sealed class MemoryCandidate
{
    public required MemoryCategory Category { get; init; }
    public required string Content { get; init; }
    public required string Source { get; init; }
    public bool RequiresUserConfirmation { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
