using System.Text.Json;
using AIChat.Application.Security;
using AIChat.Application.Tools;
using AIChat.Domain.Projects;

namespace AIChat.Application.Verification;

// Executes the same safe verification commands used by AgentHarness,
// but as an explicit user action.
public sealed class ProjectVerificationExecutor
{
    public async Task<IReadOnlyList<ProjectVerificationExecution>> RunAsync(
        string? projectPath,
        IReadOnlyList<ProjectVerificationCommand> commands,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProjectVerificationExecution>();
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toolName = ResolveToolName(command.Command);
            var tool = CreateTool(toolName);
            if (tool is null)
            {
                results.Add(new ProjectVerificationExecution(
                    command.Id, command.Name, command.Command, false,
                    "该验证命令不在安全允许列表中，未执行。", "验证命令已阻止"));
                continue;
            }

            var result = await tool.ExecuteAsync(
                BuildArgumentsJson(command, toolName),
                new AgentToolContext { ProjectPath = projectPath ?? "" },
                cancellationToken);
            var parsed = ParseResult(result);
            var safeOutput = SensitiveDataRedactor.RedactText(parsed.Output);
            var success = !result.IsError && parsed.ExitCode == 0 && !parsed.TimedOut;
            results.Add(new ProjectVerificationExecution(
                command.Id,
                command.Name,
                string.IsNullOrWhiteSpace(parsed.Command) ? command.Command : parsed.Command,
                success,
                safeOutput,
                VerificationResultParser.Summarize(safeOutput)));
        }

        return results;
    }

    internal static string ResolveToolName(string command)
    {
        var normalized = command.Trim().ToLowerInvariant();
        if (normalized == "dotnet test") return "run_test";
        if (normalized == "dotnet build") return "run_build";
        return ShellCommandTool.IsAllowlisted(command) ? "run_shell" : "";
    }

    internal static IAgentTool? CreateTool(string toolName) => toolName switch
    {
        "run_build" => new RunBuildTool(),
        "run_test" => new RunTestTool(),
        "run_shell" => new ShellCommandTool(),
        _ => null
    };

    internal static string BuildArgumentsJson(ProjectVerificationCommand command, string toolName)
    {
        if (toolName == "run_shell")
        {
            return JsonSerializer.Serialize(new
            {
                command = command.Command,
                shell = OperatingSystem.IsWindows() ? "cmd" : "auto",
                working_directory = command.WorkingDirectory,
                timeout_seconds = command.TimeoutSeconds > 0 ? command.TimeoutSeconds : 120,
                max_output_chars = 20_000
            });
        }

        return JsonSerializer.Serialize(new
        {
            target = command.WorkingDirectory,
            timeout_seconds = command.TimeoutSeconds > 0 ? command.TimeoutSeconds : 120
        });
    }

    private static ParsedVerification ParseResult(AgentToolResult result)
    {
        try
        {
            using var document = JsonDocument.Parse(result.Content);
            var root = document.RootElement;
            return new ParsedVerification(
                root.TryGetProperty("command", out var command) ? command.GetString() ?? "" : "",
                root.TryGetProperty("exitCode", out var exitCode) ? exitCode.GetInt32() : result.IsError ? 1 : 0,
                root.TryGetProperty("timedOut", out var timedOut) && timedOut.GetBoolean(),
                root.TryGetProperty("output", out var output) ? output.GetString() ?? "" : result.Content);
        }
        catch (JsonException)
        {
            return new ParsedVerification(result.ToolName, result.IsError ? 1 : 0, false, result.Content);
        }
    }

    private sealed record ParsedVerification(string Command, int ExitCode, bool TimedOut, string Output);
}

public sealed record ProjectVerificationExecution(
    string CommandId,
    string Name,
    string Command,
    bool IsSuccess,
    string Output,
    string Summary);
