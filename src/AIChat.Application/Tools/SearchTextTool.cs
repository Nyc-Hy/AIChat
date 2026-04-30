using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Application.Tools;

public sealed class SearchTextTool : IAgentTool
{
    public string Id => "search_text";
    public AgentToolRisk Risk => AgentToolRisk.ReadOnly;

    public ChatToolDefinition Definition { get; } = new()
    {
        Name = "search_text",
        Description = "在当前项目文本文件中搜索关键词，返回匹配文件、行号和片段。",
        ParametersJson = """
        {
          "type": "object",
          "required": ["query"],
          "properties": {
            "query": { "type": "string", "description": "要搜索的文本。" },
            "max_results": { "type": "integer", "description": "最多返回多少条匹配，默认 40。" }
          }
        }
        """
    };

    public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        var args = ToolJson.ParseArguments(argumentsJson);
        var query = ToolJson.GetString(args, "query") ?? "";
        return Task.FromResult(new AgentToolPreview
        {
            ToolName = Id,
            Risk = Risk,
            Summary = $"搜索文本：{query}",
            PreviewText = argumentsJson
        });
    }

    public async Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var query = ToolJson.GetString(args, "query");
            if (string.IsNullOrWhiteSpace(query))
            {
                return Error("缺少 query 参数。");
            }

            var maxResults = ToolJson.GetInt(args, "max_results", 40, 1, 120);
            var root = ProjectPathGuard.ResolveInsideProject(context.ProjectPath, "");
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git", ".vs", "bin", "obj", "artifacts", "TestResults"
            };
            var results = new List<object>();
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(skip.Contains) ||
                    IsProbablyBinary(file))
                {
                    continue;
                }

                var lineNumber = 0;
                foreach (var line in await File.ReadAllLinesAsync(file, cancellationToken))
                {
                    lineNumber++;
                    var index = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (index < 0)
                    {
                        continue;
                    }

                    results.Add(new
                    {
                        path = ProjectPathGuard.ToProjectRelativePath(context.ProjectPath, file).Replace('\\', '/'),
                        line = lineNumber,
                        preview = CreatePreview(line, index, query.Length)
                    });
                    if (results.Count >= maxResults)
                    {
                        return Success(JsonSerializer.Serialize(new { query, count = results.Count, results }));
                    }
                }
            }

            return Success(JsonSerializer.Serialize(new { query, count = results.Count, results }));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static bool IsProbablyBinary(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreatePreview(string line, int index, int length)
    {
        var start = Math.Max(0, index - 40);
        var end = Math.Min(line.Length, index + length + 40);
        var prefix = start > 0 ? "..." : "";
        var suffix = end < line.Length ? "..." : "";
        return $"{prefix}{line[start..end]}{suffix}";
    }

    private AgentToolResult Success(string content) => new() { ToolName = Id, Content = content };
    private AgentToolResult Error(string content) => new() { ToolName = Id, Content = content, IsError = true };
}
