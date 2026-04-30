using System.Text.Json;

namespace AIChat.Application.Tools;

internal static class DotnetVerification
{
    internal static async Task<AgentToolResult> RunAsync(
        string toolName,
        string projectPath,
        string verb,
        string argumentsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var args = ToolJson.ParseArguments(argumentsJson);
            var target = ToolJson.GetString(args, "target") ?? "";
            var configuration = ToolJson.GetString(args, "configuration") ?? "";
            var timeoutSeconds = ToolJson.GetInt(args, "timeout_seconds", verb == "test" ? 180 : 120, 1, 600);
            var maxOutputChars = ToolJson.GetInt(args, "max_output_chars", 20_000, 1, 80_000);
            var root = ProjectPathGuard.ResolveInsideProject(projectPath, "");
            var dotnetArgs = new List<string> { verb };

            if (!string.IsNullOrWhiteSpace(target))
            {
                var fullTarget = ProjectPathGuard.ResolveInsideProject(projectPath, target);
                if (!File.Exists(fullTarget) && !Directory.Exists(fullTarget))
                {
                    return Error(toolName, $"目标不存在：{target}");
                }

                dotnetArgs.Add(ProjectPathGuard.ToProjectRelativePath(projectPath, fullTarget));
            }

            if (!string.IsNullOrWhiteSpace(configuration))
            {
                dotnetArgs.Add("--configuration");
                dotnetArgs.Add(configuration);
            }

            dotnetArgs.Add("--nologo");
            var result = await ProcessCommand.RunAsync("dotnet", dotnetArgs, root, timeoutSeconds, cancellationToken);
            var stdout = Truncate(result.Stdout, maxOutputChars);
            var stderr = Truncate(result.Stderr, maxOutputChars);
            return new AgentToolResult
            {
                ToolName = toolName,
                IsError = result.ExitCode != 0,
                Content = JsonSerializer.Serialize(new
                {
                    command = "dotnet " + string.Join(' ', dotnetArgs.Select(QuoteIfNeeded)),
                    exitCode = result.ExitCode,
                    timedOut = result.TimedOut,
                    stdout,
                    stderr,
                    output = string.IsNullOrWhiteSpace(stderr)
                        ? stdout
                        : stdout + "\n[stderr]\n" + stderr
                })
            };
        }
        catch (Exception ex)
        {
            return Error(toolName, ex.Message);
        }
    }

    private static AgentToolResult Error(string toolName, string content)
    {
        return new AgentToolResult { ToolName = toolName, Content = content, IsError = true };
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.Length <= maxChars
            ? value
            : value[..maxChars] + $"\n...[truncated {value.Length - maxChars} chars]";
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
