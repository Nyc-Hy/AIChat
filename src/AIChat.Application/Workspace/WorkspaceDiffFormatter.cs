namespace AIChat.Application.Workspace;

public static class WorkspaceDiffFormatter
{
    public static bool ShouldShowStagedDiff(bool isStaged, bool hasUnstagedChanges)
    {
        return isStaged && !hasUnstagedChanges;
    }

    public static string FormatDiffText(WorkspaceDiff diff)
    {
        return diff.HasDiff
            ? diff.DiffText
            : "该文件没有未暂存 diff，可能只有暂存区变更或未跟踪状态。";
    }
}
