using AIChat.Application.Tools;
using AIChat.Application.Verification;

namespace AIChat.Application.Agents;

// Auto-verify tool resolution helpers — split out from the main
// AgentHarness partial so the orchestration file stays focused on
// the run loop. These helpers all live in service of
// RunAutoVerifyLoopAsync: GetVerificationToolName /
// CreateVerificationTool map a ProjectVerificationCommand to a
// concrete IAgentTool, BuildVerificationArgsJson delegates to the
// shared manual/automatic executor, and CreateToolContext wraps
// the run context for tool execution.
public sealed partial class AgentHarness
{
    private static string GetVerificationToolName(string command)
    {
        return ProjectVerificationExecutor.ResolveToolName(command);
    }

    private static IAgentTool? CreateVerificationTool(string toolName)
    {
        return ProjectVerificationExecutor.CreateTool(toolName);
    }

    private static string BuildVerificationArgsJson(Domain.Projects.ProjectVerificationCommand cmd, string toolName)
    {
        return ProjectVerificationExecutor.BuildArgumentsJson(cmd, toolName);
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
