using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Prompting;

namespace AIChat.Application.Agents.Templates;

public sealed class AgentTemplateCatalog
{
    private readonly IReadOnlyList<AgentTemplate> _templates;

    public AgentTemplateCatalog(IEnumerable<AgentTemplate>? templates = null)
    {
        _templates = (templates ?? CreateDefaults()).ToList();
    }

    public IReadOnlyList<AgentTemplate> All => _templates;

    public AgentTemplate Get(string id)
    {
        return _templates.First(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public AgentTemplate SelectForPhase(AgentRunPhase phase, bool requiresWrite = false)
    {
        var id = phase switch
        {
            AgentRunPhase.Planning => "planner",
            AgentRunPhase.GatheringContext => "explorer",
            AgentRunPhase.Verifying => "verifier",
            AgentRunPhase.Repairing => requiresWrite ? "worker" : "verifier",
            AgentRunPhase.Summarizing => "summarizer",
            _ => requiresWrite ? "worker" : "explorer"
        };
        return Get(id);
    }

    public static IReadOnlyList<AgentTemplate> CreateDefaults()
    {
        return
        [
            new AgentTemplate
            {
                Id = "planner",
                Name = "Planner",
                Description = "Creates a structured plan for the current single-agent run.",
                PromptProfile = AgentPromptProfile.Planning,
                DefaultToolIds = ["update_plan"],
                DefaultToolPermissionModes = AutoReadOnly(["update_plan"]),
                CanWrite = false,
                CanVerify = false,
                OutputSchema = "AgentStructuredPlan JSON"
            },
            new AgentTemplate
            {
                Id = "explorer",
                Name = "Explorer",
                Description = "Performs read-only codebase analysis and returns findings with context refs.",
                PromptProfile = AgentPromptProfile.ContextGathering,
                DefaultToolIds = ["list_files", "search_text", "read_file", "git_status", "git_diff"],
                DefaultToolPermissionModes = AutoReadOnly(["list_files", "search_text", "read_file", "git_status", "git_diff"]),
                CanWrite = false,
                CanVerify = false,
                OutputSchema = "summary, findings, contextRefs, recommendedNextStep"
            },
            new AgentTemplate
            {
                Id = "worker",
                Name = "Worker",
                Description = "Edits files inside an assigned write scope and reports changed files.",
                PromptProfile = AgentPromptProfile.Execution,
                DefaultToolIds = ["read_file", "search_text", "apply_patch", "edit_file", "write_file", "git_status", "git_diff"],
                DefaultToolPermissionModes = Merge(
                    AutoReadOnly(["read_file", "search_text", "git_status", "git_diff"]),
                    ConfirmEach(["apply_patch", "edit_file", "write_file"])),
                CanWrite = true,
                CanVerify = false,
                OutputSchema = "status, summary, changedFiles, artifactRefs, recommendedNextStep"
            },
            new AgentTemplate
            {
                Id = "verifier",
                Name = "Verifier",
                Description = "Runs configured checks and explains failures without editing files.",
                PromptProfile = AgentPromptProfile.VerificationRepair,
                DefaultToolIds = ["read_file", "search_text", "run_build", "run_test", "git_status", "git_diff"],
                DefaultToolPermissionModes = Merge(
                    AutoReadOnly(["read_file", "search_text", "git_status", "git_diff"]),
                    ConfirmEach(["run_build", "run_test"])),
                CanWrite = false,
                CanVerify = true,
                OutputSchema = "status, summary, verificationResults, findings, recommendedNextStep"
            },
            new AgentTemplate
            {
                Id = "summarizer",
                Name = "Summarizer",
                Description = "Compresses run results, artifacts, and next steps.",
                PromptProfile = AgentPromptProfile.Summarization,
                DefaultToolIds = [],
                DefaultToolPermissionModes = new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase),
                CanWrite = false,
                CanVerify = false,
                OutputSchema = "summary, artifacts, changedFiles, validation, nextStep"
            },
            new AgentTemplate
            {
                Id = "reviewer",
                Name = "Reviewer",
                Description = "Reviews changes for risks, bugs, missing tests, and follow-up work.",
                PromptProfile = AgentPromptProfile.Review,
                DefaultToolIds = ["read_file", "search_text", "git_status", "git_diff"],
                DefaultToolPermissionModes = AutoReadOnly(["read_file", "search_text", "git_status", "git_diff"]),
                CanWrite = false,
                CanVerify = false,
                OutputSchema = "findings, severity, evidenceRefs, testGaps, recommendation"
            }
        ];
    }

    private static Dictionary<string, ToolPermissionMode> AutoReadOnly(IEnumerable<string> toolIds)
    {
        return toolIds.ToDictionary(tool => tool, _ => ToolPermissionMode.AutoReadOnly, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, ToolPermissionMode> ConfirmEach(IEnumerable<string> toolIds)
    {
        return toolIds.ToDictionary(tool => tool, _ => ToolPermissionMode.ConfirmEachTime, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, ToolPermissionMode> Merge(params Dictionary<string, ToolPermissionMode>[] dictionaries)
    {
        var result = new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var dictionary in dictionaries)
        {
            foreach (var (toolId, mode) in dictionary)
            {
                result[toolId] = mode;
            }
        }

        return result;
    }
}
