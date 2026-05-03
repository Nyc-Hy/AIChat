namespace AIChat.Application.Workspace;

public static class WorkspaceOperationTextFormatter
{
    // --- Restore confirmations ---

    public static string RestoreSingleFileConfirm(bool isUntracked, string path)
    {
        return isUntracked
            ? $"删除未跟踪文件？\n\n{path}"
            : $"恢复该文件的未提交改动？\n\n{path}";
    }

    public static string RestoreSelectedConfirm(int count)
    {
        return $"恢复已选择的 {count} 个文件？\n\n未跟踪文件会被删除。";
    }

    // --- Restore results ---

    public static string RestoreSingleFileSuccess(bool deletedUntracked, string path)
    {
        return deletedUntracked
            ? $"已删除未跟踪文件：{path}"
            : $"已恢复文件：{path}";
    }

    public static string RestoreMultipleSuccess(int restored, int errors)
    {
        return errors == 0
            ? $"已恢复 {restored} 个已选文件"
            : $"已恢复 {restored} 个文件，{errors} 个失败";
    }

    public static string RestoreError(string message) => $"恢复失败：{message}";

    // --- Commit default messages ---

    public static string CommitSingleFileDefaultMessage(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return $"Update {fileName}";
    }

    public static string CommitMultipleDefaultMessage(int pathCount)
    {
        return $"Update {pathCount} files";
    }

    // --- Commit results ---

    public static string CommitSingleFileSuccess(WorkspaceCommitResult result)
    {
        return string.IsNullOrWhiteSpace(result.Commit)
            ? $"已提交：{result.Message}"
            : $"已提交 {result.Commit}：{result.Message}";
    }

    public static string CommitMultipleSuccess(WorkspaceCommitResult result)
    {
        return string.IsNullOrWhiteSpace(result.Commit)
            ? $"已提交 {result.Paths.Count} 个文件：{result.Message}"
            : $"已提交 {result.Commit}：{result.Message}";
    }

    public static string CommitError(string message) => $"提交失败：{message}";
}
