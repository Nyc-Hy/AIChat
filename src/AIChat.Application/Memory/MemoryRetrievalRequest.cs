using AIChat.Domain.Memory;

namespace AIChat.Application.Memory;

public sealed class MemoryRetrievalRequest
{
    public string ProjectId { get; init; } = "";
    public string Query { get; init; } = "";
    public IReadOnlySet<MemoryCategory> Categories { get; init; } = new HashSet<MemoryCategory>
    {
        MemoryCategory.Project,
        MemoryCategory.Task,
        MemoryCategory.Tool
    };
    public int MaxResults { get; init; } = 6;
}
