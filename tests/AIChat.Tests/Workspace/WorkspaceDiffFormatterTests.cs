using AIChat.Application.Workspace;

namespace AIChat.Tests.Workspace;

public sealed class WorkspaceDiffFormatterTests
{
    [Theory]
    [InlineData(true, false, true)]   // staged only → show staged diff
    [InlineData(true, true, false)]   // staged + unstaged → show unstaged diff
    [InlineData(false, false, false)] // unstaged only → not staged
    [InlineData(false, true, false)]  // unstaged only → not staged
    public void ShouldShowStagedDiff_ReturnsExpected(bool isStaged, bool hasUnstaged, bool expected)
    {
        Assert.Equal(expected, WorkspaceDiffFormatter.ShouldShowStagedDiff(isStaged, hasUnstaged));
    }

    [Fact]
    public void FormatDiffText_WhenHasDiff_ReturnsDiffText()
    {
        var diff = new WorkspaceDiff { DiffText = "--- a/foo.cs\n+++ b/foo.cs\n@@ -1 +1 @@\n-old\n+new" };

        var result = WorkspaceDiffFormatter.FormatDiffText(diff);

        Assert.Contains("+++ b/foo.cs", result);
    }

    [Fact]
    public void FormatDiffText_WhenNoDiff_ReturnsFallbackMessage()
    {
        var diff = new WorkspaceDiff { DiffText = "" };

        var result = WorkspaceDiffFormatter.FormatDiffText(diff);

        Assert.Contains("没有未暂存 diff", result);
    }

    [Fact]
    public void FormatDiffText_WhenDiffTextIsNull_ReturnsFallbackMessage()
    {
        var diff = new WorkspaceDiff { DiffText = null! };

        var result = WorkspaceDiffFormatter.FormatDiffText(diff);

        Assert.Contains("没有未暂存 diff", result);
    }
}
