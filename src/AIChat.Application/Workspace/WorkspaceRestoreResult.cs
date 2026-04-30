namespace AIChat.Application.Workspace;

public sealed class WorkspaceRestoreResult
{
    public string Path { get; init; } = "";
    public bool Restored { get; init; }
    public bool DeletedUntracked { get; init; }
}
