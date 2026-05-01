namespace AIChat.Application.Workspace;

public sealed class ProjectFileIndex
{
    public string RootPath { get; init; } = "";
    public IReadOnlyList<ProjectFileIndexEntry> Entries { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
    public bool WasTruncated { get; init; }
}
