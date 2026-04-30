namespace AIChat.Application.Workspace;

public sealed class WorkspaceChangeSet
{
    public string Branch { get; init; } = "";
    public IReadOnlyList<WorkspaceChange> Changes { get; init; } = [];
    public bool IsTruncated { get; init; }
    public bool HasChanges => Changes.Count > 0;
}
