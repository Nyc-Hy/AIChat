using AIChat.Application.Tools;

namespace AIChat.Tests.Tools;

public sealed class ProjectPathGuardTests
{
    [Fact]
    public void ResolveInsideProject_AllowsProjectRelativePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "AIChat.PathGuard.Tests");

        var result = ProjectPathGuard.ResolveInsideProject(root, "src/App.cs");

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "src", "App.cs")), result);
    }

    [Fact]
    public void ResolveInsideProject_RejectsPathTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "AIChat.PathGuard.Tests");
        var traversal = $"..{Path.DirectorySeparatorChar}outside.txt";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProjectPathGuard.ResolveInsideProject(root, traversal));

        Assert.Contains("项目范围", ex.Message);
    }

    [Fact]
    public void EnsureWritableProjectPath_RejectsProtectedDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "AIChat.PathGuard.Tests");
        var fullPath = Path.GetFullPath(Path.Combine(root, ".git", "config"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProjectPathGuard.EnsureWritableProjectPath(root, fullPath));

        Assert.Contains("受保护目录", ex.Message);
    }
}
