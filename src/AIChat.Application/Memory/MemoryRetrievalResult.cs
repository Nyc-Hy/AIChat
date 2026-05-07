using AIChat.Domain.Memory;

namespace AIChat.Application.Memory;

public sealed record MemoryRetrievalResult(MemoryEntry Entry, double Score, string Reason);
