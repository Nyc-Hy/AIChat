using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class GitStatusTool : IAgentTool
{
    public string Id => "git_status";
    public AgentToolRisk Risk => AgentToolRisk.ReadOnly;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "git_status",
        Description = "读取当前项目的 git 状态，返回分支信息和变更文件列表。只执行 git status，不修改仓库。",
        ParametersJson = """
        {
          "type": "object",
          "properties": {
            "max_files": { "type": "integer", "description": "最多返回多少个文件，默认 120。" }
          }
        }
        """
    };

    public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = "读取 git 状态",
            PreviewText = "git status --short --branch --untracked-files=all"
        });
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var maxFiles = ToolJson.GetInt(args, "max_files", 120, 1, 500);
            var result = await GitCommand.RunAsync(
                context.ProjectPath,
                ["status", "--short", "--branch", "--untracked-files=all"],
                timeoutSeconds: 15,
                cancellationToken);
            if (result.ExitCode != 0)
            {
                return Error(CreateErrorPayload(result));
            }

            var lines = result.Stdout.ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
            var branch = lines.FirstOrDefault(line => line.StartsWith("## ", StringComparison.Ordinal)) ?? "";
            var files = lines
                .Where(line => !line.StartsWith("## ", StringComparison.Ordinal))
                .Take(maxFiles)
                .Select(ParseStatusLine)
                .ToList();

            return Success(JsonSerializer.Serialize(new
            {
                branch,
                count = files.Count,
                truncated = lines.Count - (string.IsNullOrWhiteSpace(branch) ? 0 : 1) > files.Count,
                files
            }));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static object ParseStatusLine(string line)
    {
        var status = line.Length >= 2 ? line[..2] : line;
        var path = line.Length > 3 ? line[3..] : "";
        return new
        {
            status,
            path = path.Replace('\\', '/')
        };
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
