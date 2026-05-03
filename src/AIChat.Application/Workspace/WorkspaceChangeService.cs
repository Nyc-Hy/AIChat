using AIChat.Application.Tools;

namespace AIChat.Application.Workspace;

public interface IWorkspaceChangeService
{
    Task<WorkspaceRestoreResult> RestoreFileAsync(
        string projectPath,
        string path,
        bool deleteUntracked = false,
        CancellationToken cancellationToken = default);

    Task<WorkspaceCommitResult> CommitAsync(
        string projectPath,
        string message,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);

    Task StageAsync(
        string projectPath,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);

    Task UnstageAsync(
        string projectPath,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceChangeService : IWorkspaceChangeService
{
    public async Task<WorkspaceChangeSet> GetChangesAsync(
        string projectPath,
        int maxFiles = 200,
        CancellationToken cancellationToken = default)
    {
        var result = await GitCommand.RunAsync(
            projectPath,
            ["status", "--short", "--branch", "--untracked-files=all"],
            timeoutSeconds: 15,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CreateErrorMessage(result));
        }

        var lines = result.Stdout.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        var branch = lines.FirstOrDefault(line => line.StartsWith("## ", StringComparison.Ordinal)) ?? "";
        var fileLines = lines
            .Where(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .ToList();
        var changes = fileLines
            .Take(Math.Clamp(maxFiles, 1, 1_000))
            .Select(ParseStatusLine)
            .Where(change => !string.IsNullOrWhiteSpace(change.Path))
            .ToList();

        return new WorkspaceChangeSet
        {
            Branch = branch,
            Changes = changes,
            IsTruncated = fileLines.Count > changes.Count
        };
    }

    public async Task<WorkspaceDiff> GetDiffAsync(
        string projectPath,
        string path,
        bool staged = false,
        int maxChars = 40_000,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "diff" };
        if (staged)
        {
            arguments.Add("--cached");
        }

        var normalizedPath = "";
        if (!string.IsNullOrWhiteSpace(path))
        {
            var fullPath = ProjectPathGuard.ResolveInsideProject(projectPath, path);
            normalizedPath = ProjectPathGuard.ToProjectRelativePath(projectPath, fullPath).Replace('\\', '/');
            var status = await GetPathStatusAsync(projectPath, normalizedPath, cancellationToken);
            if (status.IsUntracked)
            {
                var content = File.Exists(fullPath)
                    ? await File.ReadAllTextAsync(fullPath, cancellationToken)
                    : "";
                var untrackedDiff = ToolDiff.CreateUnifiedDiff(normalizedPath, "", content, maxLines: 260);
                var untrackedTruncated = untrackedDiff.Length > maxChars;
                if (untrackedTruncated)
                {
                    untrackedDiff = untrackedDiff[..maxChars];
                }

                return new WorkspaceDiff
                {
                    Path = normalizedPath,
                    Staged = staged,
                    DiffText = untrackedDiff,
                    IsTruncated = untrackedTruncated
                };
            }

            arguments.Add("--");
            arguments.Add(normalizedPath);
        }

        var result = await GitCommand.RunAsync(projectPath, arguments, timeoutSeconds: 20, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CreateErrorMessage(result));
        }

        var diff = result.Stdout.ReplaceLineEndings("\n");
        var truncated = diff.Length > maxChars;
        if (truncated)
        {
            diff = diff[..maxChars];
        }

        return new WorkspaceDiff
        {
            Path = normalizedPath,
            Staged = staged,
            DiffText = diff,
            IsTruncated = truncated
        };
    }

    public async Task<WorkspaceRestoreResult> RestoreFileAsync(
        string projectPath,
        string path,
        bool deleteUntracked = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ProjectPathGuard.ResolveInsideProject(projectPath, path);
        ProjectPathGuard.EnsureWritableProjectPath(projectPath, fullPath);
        var normalizedPath = ProjectPathGuard.ToProjectRelativePath(projectPath, fullPath).Replace('\\', '/');
        var status = await GetPathStatusAsync(projectPath, normalizedPath, cancellationToken);
        if (!status.HasChanges)
        {
            throw new InvalidOperationException($"文件没有未提交改动：{normalizedPath}");
        }

        if (status.IsUntracked)
        {
            if (!deleteUntracked)
            {
                throw new InvalidOperationException($"文件未被 Git 跟踪。如需删除，请明确允许删除：{normalizedPath}");
            }

            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException($"未跟踪路径不是文件，未删除：{normalizedPath}");
            }

            File.Delete(fullPath);
            return new WorkspaceRestoreResult
            {
                Path = normalizedPath,
                DeletedUntracked = true
            };
        }

        var result = await GitCommand.RunAsync(
            projectPath,
            ["restore", "--staged", "--worktree", "--", normalizedPath],
            timeoutSeconds: 20,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CreateErrorMessage(result));
        }

        return new WorkspaceRestoreResult
        {
            Path = normalizedPath,
            Restored = true
        };
    }

    public async Task<WorkspaceCommitResult> CommitAsync(
        string projectPath,
        string message,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("提交信息不能为空。");
        }

        var normalizedPaths = NormalizePaths(projectPath, paths);
        if (normalizedPaths.Count == 0)
        {
            throw new InvalidOperationException("至少需要一个明确文件路径。");
        }

        var addResult = await GitCommand.RunAsync(
            projectPath,
            ["add", "--", .. normalizedPaths],
            timeoutSeconds: 20,
            cancellationToken);
        if (addResult.ExitCode != 0)
        {
            throw new InvalidOperationException(CreateErrorMessage(addResult));
        }

        var commitResult = await GitCommand.RunAsync(
            projectPath,
            ["commit", "-m", message.Trim()],
            timeoutSeconds: 30,
            cancellationToken);
        if (commitResult.ExitCode != 0)
        {
            throw new InvalidOperationException(CreateErrorMessage(commitResult));
        }

        var hashResult = await GitCommand.RunAsync(
            projectPath,
            ["rev-parse", "--short", "HEAD"],
            timeoutSeconds: 10,
            cancellationToken);

        return new WorkspaceCommitResult
        {
            Commit = hashResult.ExitCode == 0 ? hashResult.Stdout.Trim() : "",
            Message = message.Trim(),
            Paths = normalizedPaths
        };
    }

    public async Task StageAsync(
        string projectPath,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        var normalizedPaths = NormalizePaths(projectPath, paths);
        if (normalizedPaths.Count == 0)
        {
            throw new InvalidOperationException("至少需要一个明确文件路径。");
        }

        var result = await GitCommand.RunAsync(
            projectPath,
            ["add", "--", .. normalizedPaths],
            timeoutSeconds: 20,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CreateErrorMessage(result));
        }
    }

    public async Task UnstageAsync(
        string projectPath,
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        var normalizedPaths = NormalizePaths(projectPath, paths);
        if (normalizedPaths.Count == 0)
        {
            throw new InvalidOperationException("至少需要一个明确文件路径。");
        }

        var result = await GitCommand.RunAsync(
            projectPath,
            ["restore", "--staged", "--", .. normalizedPaths],
            timeoutSeconds: 20,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CreateErrorMessage(result));
        }
    }

    private static WorkspaceChange ParseStatusLine(string line)
    {
        var status = line.Length >= 2 ? line[..2] : line;
        var path = line.Length > 3 ? line[3..] : "";
        return new WorkspaceChange
        {
            Status = status,
            Path = path.Replace('\\', '/')
        };
    }

    private static IReadOnlyList<string> NormalizePaths(string projectPath, IReadOnlyList<string> paths)
    {
        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path =>
            {
                var fullPath = ProjectPathGuard.ResolveInsideProject(projectPath, path);
                ProjectPathGuard.EnsureWritableProjectPath(projectPath, fullPath);
                return ProjectPathGuard.ToProjectRelativePath(projectPath, fullPath).Replace('\\', '/');
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<PathGitStatus> GetPathStatusAsync(
        string projectPath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var result = await GitCommand.RunAsync(
            projectPath,
            ["status", "--porcelain", "--untracked-files=all", "--", relativePath],
            timeoutSeconds: 15,
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CreateErrorMessage(result));
        }

        var lines = result.Stdout.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return new PathGitStatus(
            lines.Length > 0,
            lines.Any(line => line.StartsWith("?? ", StringComparison.Ordinal)));
    }

    private static string CreateErrorMessage(GitCommandResult result)
    {
        return string.IsNullOrWhiteSpace(result.Stderr)
            ? result.Stdout
            : result.Stderr;
    }

    private sealed record PathGitStatus(bool HasChanges, bool IsUntracked);
}
