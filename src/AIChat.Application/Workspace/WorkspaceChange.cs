namespace AIChat.Application.Workspace;

public sealed class WorkspaceChange
{
    public string Status { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsStaged => Status.Length > 0 && Status[0] != ' ' && Status[0] != '?';
    public bool IsUntracked => Status.StartsWith("??", StringComparison.Ordinal);
    public string DisplayStatus => IsUntracked ? "未跟踪" : Status.Trim();
}
