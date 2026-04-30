using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class GitDiffTool : IAgentTool
{
    public string Id => "git_diff";
    public AgentToolRisk Risk => AgentToolRisk.ReadOnly;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "git_diff",
        Description = "读取当前项目的 git diff。可选择 staged 或指定单个项目内文件路径。只读取 diff，不修改仓库。",
        ParametersJson = """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "可选，相对项目根目录的文件路径。" },
            "staged": { "type": "boolean", "description": "是否读取暂存区 diff，默认 false。" },
            "max_chars": { "type": "integer", "description": "最多返回多少字符，默认 20000。" }
          }
        }
        """
    };

    public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var path = ToolJson.GetString(args, "path") ?? "";
        var staged = ToolJson.GetBool(args, "staged", defaultValue: false);
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = string.IsNullOrWhiteSpace(path) ? "读取 git diff" : $"读取 git diff：{path}",
            PreviewText = staged ? "git diff --cached" : "git diff"
        });
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var path = ToolJson.GetString(args, "path") ?? "";
            var staged = ToolJson.GetBool(args, "staged", defaultValue: false);
            var maxChars = ToolJson.GetInt(args, "max_chars", 20_000, 1, 80_000);
            var gitArguments = new List<string> { "diff" };
            if (staged)
            {
                gitArguments.Add("--cached");
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                var fullPath = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, path);
                var relative = ProjectPathGuard.ToProjectRelativePath(context.ProjectPath, fullPath).Replace('\\', '/');
                gitArguments.Add("--");
                gitArguments.Add(relative);
            }

            var result = await GitCommand.RunAsync(context.ProjectPath, gitArguments, timeoutSeconds: 20, cancellationToken);
            if (result.ExitCode != 0)
            {
                return Error(CreateErrorPayload(result));
            }

            var diff = result.Stdout.ReplaceLineEndings("\n");
            var truncated = diff.Length > maxChars;
            if (truncated)
            {
                diff = diff[..maxChars];
            }

            return Success(JsonSerializer.Serialize(new
            {
                path = string.IsNullOrWhiteSpace(path) ? null : path.Replace('\\', '/'),
                staged,
                chars = diff.Length,
                truncated,
                diff
            }));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
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
}
