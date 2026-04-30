namespace AIChat.Application.Workspace;

public sealed class WorkspaceDiff
{
    public string Path { get; init; } = "";
    public bool Staged { get; init; }
    public string DiffText { get; init; } = "";
    public bool IsTruncated { get; init; }
    public bool HasDiff => !string.IsNullOrWhiteSpace(DiffText);
}
