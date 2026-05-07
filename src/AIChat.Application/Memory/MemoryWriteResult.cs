using AIChat.Domain.Memory;

namespace AIChat.Application.Memory;

public sealed class MemoryWriteResult
{
    public bool IsStored { get; init; }
    public string Reason { get; init; } = "";
    public MemoryEntry? Entry { get; init; }

    public static MemoryWriteResult Stored(MemoryEntry entry) => new()
    {
        IsStored = true,
        Entry = entry
    };

    public static MemoryWriteResult Rejected(string reason) => new()
    {
        IsStored = false,
        Reason = reason
    };
}
