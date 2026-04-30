namespace AIChat.Application.Workspace;

public sealed class WorkspaceCommitResult
{
    public string Commit { get; init; } = "";
    public string Message { get; init; } = "";
    public IReadOnlyList<string> Paths { get; init; } = [];
}
