namespace AIChat.Application.Workspace;

public sealed class ProjectFileIndexEntry
{
    public string RelativePath { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Extension { get; init; } = "";
    public string TypeTag { get; init; } = "asset";
}
