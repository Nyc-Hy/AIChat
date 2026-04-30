namespace AIChat.Application.Tools;

internal static class ProjectPathGuard
{
    private static readonly HashSet<string> GeneratedOrPrivateSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "artifacts", "TestResults"
    };

    internal static string ResolveInsideProject(string projectPath, string relativePath)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(projectPath)
            ? Environment.CurrentDirectory
            : projectPath);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath ?? ""));

        if (!candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("路径超出当前项目范围。");
        }

        return candidate;
    }

    internal static string ToProjectRelativePath(string projectPath, string fullPath)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(projectPath)
            ? Environment.CurrentDirectory
            : projectPath);
        return Path.GetRelativePath(root, fullPath);
    }

    internal static void EnsureWritableProjectPath(string projectPath, string fullPath)
    {
        _ = ResolveInsideProject(projectPath, ToProjectRelativePath(projectPath, fullPath));
        var relative = ToProjectRelativePath(projectPath, fullPath);
        if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(GeneratedOrPrivateSegments.Contains))
        {
            throw new InvalidOperationException("写入目标位于受保护目录（.git、.vs、bin、obj、artifacts 或 TestResults）。");
        }
    }
}
