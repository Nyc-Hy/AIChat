using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class GitCommitTool : IAgentTool
{
    public string Id => "git_commit";
    public AgentToolRisk Risk => AgentToolRisk.Write;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "git_commit",
        Description = "提交当前项目中用户明确指定的文件。必须提供非空 message 和 paths；工具会先 git add 指定路径，再 git commit。",
        ParametersJson = """
        {
          "type": "object",
          "required": ["message", "paths"],
          "properties": {
            "message": { "type": "string", "description": "Git commit message。" },
            "paths": {
              "type": "array",
              "description": "要暂存并提交的项目内相对路径列表。",
              "items": { "type": "string" }
            }
          }
        }
        """
    };

    public async Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ParseArgs(argumentsJson, context.ProjectPath);
        var status = await GitCommand.RunAsync(
            context.ProjectPath,
            ["status", "--short", "--untracked-files=all", "--", .. args.Paths],
            timeoutSeconds: 15,
            cancellationToken);
        var diff = await GitCommand.RunAsync(
            context.ProjectPath,
            ["diff", "--", .. args.Paths],
            timeoutSeconds: 20,
            cancellationToken);

        return new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"提交 {args.Paths.Count} 个路径",
            PreviewText = JsonSerializer.Serialize(new
            {
                message = args.Message,
                paths = args.Paths,
                status = status.Stdout.ReplaceLineEndings("\n")
            }),
            DiffText = diff.Stdout.ReplaceLineEndings("\n")
        };
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ParseArgs(argumentsJson, context.ProjectPath);
            if (string.IsNullOrWhiteSpace(args.Message))
            {
                return Error("缺少 message 参数，或 message 为空。");
            }

            if (args.Paths.Count == 0)
            {
                return Error("缺少 paths 参数，至少需要一个明确文件路径。");
            }

            var addResult = await GitCommand.RunAsync(
                context.ProjectPath,
                ["add", "--", .. args.Paths],
                timeoutSeconds: 20,
                cancellationToken);
            if (addResult.ExitCode != 0)
            {
                return Error(CreateErrorPayload(addResult));
            }

            var commitResult = await GitCommand.RunAsync(
                context.ProjectPath,
                ["commit", "-m", args.Message],
                timeoutSeconds: 30,
                cancellationToken);
            if (commitResult.ExitCode != 0)
            {
                return Error(CreateErrorPayload(commitResult));
            }

            var hashResult = await GitCommand.RunAsync(
                context.ProjectPath,
                ["rev-parse", "--short", "HEAD"],
                timeoutSeconds: 10,
                cancellationToken);

            return Success(JsonSerializer.Serialize(new
            {
                commit = hashResult.ExitCode == 0 ? hashResult.Stdout.Trim() : "",
                message = args.Message,
                paths = args.Paths,
                stdout = commitResult.Stdout,
                stderr = commitResult.Stderr
            }));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static CommitArgs ParseArgs(string argumentsJson, string projectPath)
    {
        var root = ToolJson.ParseArguments(argumentsJson);
        var message = ToolJson.GetString(root, "message") ?? "";
        var paths = new List<string>();
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("paths", out var pathsElement) &&
            pathsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pathsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var path = item.GetString();
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var fullPath = ProjectPathGuard.ResolveInsideProject(projectPath, path);
                ProjectPathGuard.EnsureWritableProjectPath(projectPath, fullPath);
                paths.Add(ProjectPathGuard.ToProjectRelativePath(projectPath, fullPath).Replace('\\', '/'));
            }
        }

        return new CommitArgs(message.Trim(), paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
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

    private sealed record CommitArgs(string Message, IReadOnlyList<string> Paths);
}
