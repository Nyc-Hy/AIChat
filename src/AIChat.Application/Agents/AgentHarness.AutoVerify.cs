using System.Text.Json;
using AIChat.Application.Tools;

namespace AIChat.Application.Agents;

// Auto-verify tool resolution helpers — split out from the main
// AgentHarness partial so the orchestration file stays focused on
// the run loop. These helpers all live in service of
// RunAutoVerifyLoopAsync: GetVerificationToolName /
// CreateVerificationTool map a ProjectVerificationCommand to a
// concrete IAgentTool, BuildVerificationArgsJson /
// BuildDotnetVerificationArgsJson / EscapeJson serialize the
// per-tool call payload, and CreateToolContext wraps the run
// context for tool execution.
public sealed partial class AgentHarness
{
    private static string GetVerificationToolName(string command)
    {
        var normalized = command.Trim().ToLowerInvariant();
        if (string.Equals(normalized, "dotnet test", StringComparison.Ordinal))
        {
            return "run_test";
        }

        if (string.Equals(normalized, "dotnet build", StringComparison.Ordinal))
        {
            return "run_build";
        }

        return ShellCommandTool.IsAllowlisted(command) ? "run_shell" : "";
    }

    private static IAgentTool? CreateVerificationTool(string toolName)
    {
        return toolName switch
        {
            "run_build" => new RunBuildTool(),
            "run_test" => new RunTestTool(),
            "run_shell" => new ShellCommandTool(),
            _ => null
        };
    }

    private static string BuildVerificationArgsJson(Domain.Projects.ProjectVerificationCommand cmd, string toolName)
    {
        if (string.Equals(toolName, "run_shell", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                command = cmd.Command,
                shell = OperatingSystem.IsWindows() ? "cmd" : "auto",
                working_directory = cmd.WorkingDirectory,
                timeout_seconds = cmd.TimeoutSeconds > 0 ? cmd.TimeoutSeconds : 120,
                max_output_chars = 20_000
            });
        }

        return BuildDotnetVerificationArgsJson(cmd);
    }

    private static string BuildDotnetVerificationArgsJson(Domain.Projects.ProjectVerificationCommand cmd)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        var first = true;
        if (!string.IsNullOrWhiteSpace(cmd.WorkingDirectory))
        {
            sb.Append($"\"target\":\"{EscapeJson(cmd.WorkingDirectory)}\"");
            first = false;
        }

        if (cmd.TimeoutSeconds > 0 && cmd.TimeoutSeconds != 120)
        {
            if (!first) sb.Append(',');
            sb.Append($"\"timeout_seconds\":{cmd.TimeoutSeconds}");
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static AgentToolContext CreateToolContext(AgentRunContext context)
    {
        return new AgentToolContext
        {
            ProjectPath = context.ProjectPath,
            InputArtifacts = context.InputArtifacts
        };
    }
}
