using AIChat.Application.Tools;

namespace AIChat.Application.Workspace;

public sealed class WorkspaceChangeService
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

    private static string CreateErrorMessage(GitCommandResult result)
    {
        return string.IsNullOrWhiteSpace(result.Stderr)
            ? result.Stdout
            : result.Stderr;
    }
}
