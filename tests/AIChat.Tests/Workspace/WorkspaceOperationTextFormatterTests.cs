using AIChat.Application.Workspace;

namespace AIChat.Tests.Workspace;

public sealed class WorkspaceOperationTextFormatterTests
{
    // --- Restore confirmations ---

    [Fact]
    public void RestoreSingleFileConfirm_Untracked_AsksToDelete()
    {
        var result = WorkspaceOperationTextFormatter.RestoreSingleFileConfirm(isUntracked: true, "src/New.cs");

        Assert.Contains("删除未跟踪文件", result);
        Assert.Contains("src/New.cs", result);
    }

    [Fact]
    public void RestoreSingleFileConfirm_Tracked_AsksToRestore()
    {
        var result = WorkspaceOperationTextFormatter.RestoreSingleFileConfirm(isUntracked: false, "src/Foo.cs");

        Assert.Contains("恢复该文件的未提交改动", result);
        Assert.Contains("src/Foo.cs", result);
    }

    [Fact]
    public void RestoreSelectedConfirm_ShowsCount()
    {
        var result = WorkspaceOperationTextFormatter.RestoreSelectedConfirm(3);

        Assert.Contains("恢复已选择的 3 个文件", result);
        Assert.Contains("未跟踪文件会被删除", result);
    }

    // --- Restore results ---

    [Fact]
    public void RestoreSingleFileSuccess_Untracked_ReportsDeleted()
    {
        var result = WorkspaceOperationTextFormatter.RestoreSingleFileSuccess(deletedUntracked: true, "src/New.cs");

        Assert.Equal("已删除未跟踪文件：src/New.cs", result);
    }

    [Fact]
    public void RestoreSingleFileSuccess_Tracked_ReportsRestored()
    {
        var result = WorkspaceOperationTextFormatter.RestoreSingleFileSuccess(deletedUntracked: false, "src/Foo.cs");

        Assert.Equal("已恢复文件：src/Foo.cs", result);
    }

    [Fact]
    public void RestoreMultipleSuccess_AllSucceeded_ReportsCount()
    {
        var result = WorkspaceOperationTextFormatter.RestoreMultipleSuccess(restored: 5, errors: 0);

        Assert.Equal("已恢复 5 个已选文件", result);
    }

    [Fact]
    public void RestoreMultipleSuccess_SomeFailed_ReportsBoth()
    {
        var result = WorkspaceOperationTextFormatter.RestoreMultipleSuccess(restored: 3, errors: 2);

        Assert.Equal("已恢复 3 个文件，2 个失败", result);
    }

    [Fact]
    public void RestoreError_IncludesMessage()
    {
        var result = WorkspaceOperationTextFormatter.RestoreError("disk full");

        Assert.Contains("恢复失败", result);
        Assert.Contains("disk full", result);
    }

    // --- Commit default messages ---

    [Fact]
    public void CommitSingleFileDefaultMessage_UsesFileName()
    {
        var result = WorkspaceOperationTextFormatter.CommitSingleFileDefaultMessage("src/Foo/Bar.cs");

        Assert.Equal("Update Bar.cs", result);
    }

    [Fact]
    public void CommitMultipleDefaultMessage_UsesCount()
    {
        var result = WorkspaceOperationTextFormatter.CommitMultipleDefaultMessage(4);

        Assert.Equal("Update 4 files", result);
    }

    // --- Commit results ---

    [Fact]
    public void CommitSingleFileSuccess_WithCommitHash_IncludesHash()
    {
        var result = WorkspaceOperationTextFormatter.CommitSingleFileSuccess(
            new WorkspaceCommitResult { Commit = "abc1234", Message = "fix bug" });

        Assert.Contains("abc1234", result);
        Assert.Contains("fix bug", result);
    }

    [Fact]
    public void CommitSingleFileSuccess_EmptyCommit_OmitsHash()
    {
        var result = WorkspaceOperationTextFormatter.CommitSingleFileSuccess(
            new WorkspaceCommitResult { Commit = "", Message = "WIP" });

        Assert.Equal("已提交：WIP", result);
    }

    [Fact]
    public void CommitMultipleSuccess_WithCommitHash_IncludesHash()
    {
        var result = WorkspaceOperationTextFormatter.CommitMultipleSuccess(
            new WorkspaceCommitResult { Commit = "def5678", Message = "update", Paths = ["a.cs", "b.cs"] });

        Assert.Contains("def5678", result);
        Assert.Contains("update", result);
    }

    [Fact]
    public void CommitMultipleSuccess_EmptyCommit_UsesPathCount()
    {
        var result = WorkspaceOperationTextFormatter.CommitMultipleSuccess(
            new WorkspaceCommitResult { Commit = "", Message = "WIP", Paths = ["x.cs", "y.cs", "z.cs"] });

        Assert.Contains("3 个文件", result);
    }

    [Fact]
    public void CommitError_IncludesMessage()
    {
        var result = WorkspaceOperationTextFormatter.CommitError("nothing to commit");

        Assert.Contains("提交失败", result);
        Assert.Contains("nothing to commit", result);
    }
}
