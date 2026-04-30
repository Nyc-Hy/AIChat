using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class GitRestoreFileTool : IAgentTool
{
    public string Id => "git_restore_file";
    public AgentToolRisk Risk => AgentToolRisk.Write;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "git_restore_file",
        Description = "回滚当前项目内单个文件的未提交改动。默认只恢复 Git 已跟踪文件；删除未跟踪文件必须显式传 delete_untracked=true。",
        ParametersJson = """
        {
          "type": "object",
          "required": ["path"],
          "properties": {
            "path": { "type": "string", "description": "相对项目根目录的文件路径。" },
            "delete_untracked": { "type": "boolean", "description": "是否允许删除未跟踪文件，默认 false。" }
          }
        }
        """
    };

    public async Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var path = ToolJson.GetString(args, "path") ?? "";
        var relativePath = ResolveWritableRelativePath(context.ProjectPath, path);
        var status = await GetPathStatusAsync(context.ProjectPath, relativePath, cancellationToken);
        var deleteUntracked = ToolJson.GetBool(args, "delete_untracked", defaultValue: false);

        return new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = status.IsUntracked && deleteUntracked
                ? $"删除未跟踪文件：{relativePath}"
                : $"恢复文件：{relativePath}",
            PreviewText = status.IsUntracked
                ? "未跟踪文件需要 delete_untracked=true 才会删除。"
                : "git restore --staged --worktree -- <path>",
            DiffText = await CreatePreviewDiffAsync(context.ProjectPath, relativePath, status, cancellationToken)
        };
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var path = ToolJson.GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path))
            {
                return Error("缺少 path 参数。");
            }

            var relativePath = ResolveWritableRelativePath(context.ProjectPath, path);
            var fullPath = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, relativePath);
            var status = await GetPathStatusAsync(context.ProjectPath, relativePath, cancellationToken);
            if (!status.HasChanges)
            {
                return Error($"文件没有未提交改动：{relativePath}");
            }

            var oldChars = File.Exists(fullPath)
                ? (await File.ReadAllTextAsync(fullPath, cancellationToken)).Length
                : 0;

            if (status.IsUntracked)
            {
                var deleteUntracked = ToolJson.GetBool(args, "delete_untracked", defaultValue: false);
                if (!deleteUntracked)
                {
                    return Error($"文件未被 Git 跟踪。如需删除，请显式传 delete_untracked=true：{relativePath}");
                }

                if (!File.Exists(fullPath))
                {
                    return Error($"未跟踪路径不是文件，未删除：{relativePath}");
                }

                File.Delete(fullPath);
            }
            else
            {
                var restoreResult = await GitCommand.RunAsync(
                    context.ProjectPath,
                    ["restore", "--staged", "--worktree", "--", relativePath],
                    timeoutSeconds: 20,
                    cancellationToken);
                if (restoreResult.ExitCode != 0)
                {
                    return Error(CreateErrorPayload(restoreResult));
                }
            }

            var newChars = File.Exists(fullPath)
                ? (await File.ReadAllTextAsync(fullPath, cancellationToken)).Length
                : 0;

            return Success(JsonSerializer.Serialize(new
            {
                path = relativePath,
                restored = !status.IsUntracked,
                deletedUntracked = status.IsUntracked,
                oldChars,
                newChars
            }));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static string ResolveWritableRelativePath(string projectPath, string path)
    {
        var fullPath = ProjectPathGuard.ResolveInsideProject(projectPath, path);
        ProjectPathGuard.EnsureWritableProjectPath(projectPath, fullPath);
        return ProjectPathGuard.ToProjectRelativePath(projectPath, fullPath).Replace('\\', '/');
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
            throw new InvalidOperationException(CreateErrorPayload(result));
        }

        var lines = result.Stdout.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var isUntracked = lines.Any(line => line.StartsWith("?? ", StringComparison.Ordinal));
        return new PathGitStatus(lines.Length > 0, isUntracked);
    }

    private static async Task<string> CreatePreviewDiffAsync(
        string projectPath,
        string relativePath,
        PathGitStatus status,
        CancellationToken cancellationToken)
    {
        if (status.IsUntracked)
        {
            var fullPath = ProjectPathGuard.ResolveInsideProject(projectPath, relativePath);
            var content = File.Exists(fullPath)
                ? await File.ReadAllTextAsync(fullPath, cancellationToken)
                : "";
            return ToolDiff.CreateUnifiedDiff(relativePath, content, "");
        }

        var unstaged = await GitCommand.RunAsync(
            projectPath,
            ["diff", "--", relativePath],
            timeoutSeconds: 20,
            cancellationToken);
        if (unstaged.ExitCode != 0)
        {
            return CreateErrorPayload(unstaged);
        }

        var staged = await GitCommand.RunAsync(
            projectPath,
            ["diff", "--cached", "--", relativePath],
            timeoutSeconds: 20,
            cancellationToken);
        if (staged.ExitCode != 0)
        {
            return CreateErrorPayload(staged);
        }

        return string.Join(
            Environment.NewLine,
            new[] { staged.Stdout.ReplaceLineEndings("\n"), unstaged.Stdout.ReplaceLineEndings("\n") }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string CreateErrorPayload(GitCommandResult result)
    {
        return JsonSerializer.Serialize(new
        {
            exitCode = result.ExitCode,
            timedOut = result.TimedOut,
            stdout = result.Stdout,
            stderr = result.Stderr
        });
    }

    private AgentToolResult Success(string content) => new() { ToolName = Id, Content = content };
    private AgentToolResult Error(string content) => new() { ToolName = Id, Content = content, IsError = true };

    private sealed record PathGitStatus(bool HasChanges, bool IsUntracked);
}
