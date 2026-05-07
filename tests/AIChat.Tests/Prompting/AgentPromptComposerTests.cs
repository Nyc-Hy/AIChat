using AIChat.Abstractions.Configuration;
using AIChat.Application.Prompting;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Prompting;

public sealed class AgentPromptComposerTests
{
    [Fact]
    public void Compose_PlanningPromptContainsJsonRequirementsAndAllowedTools()
    {
        var composition = new AgentPromptComposer().Compose(new AgentPromptComposeRequest
        {
            Profile = AgentPromptProfile.Planning,
            Goal = "add structured prompts",
            AllowedTools = ["read_file", "apply_patch"],
            ConversationMessages =
            [
                new ChatMessage { Role = ChatRole.User, Content = "please continue" }
            ]
        });

        Assert.Equal(AgentPromptProfile.Planning, composition.Profile);
        Assert.Equal(2, composition.Messages.Count);
        Assert.Contains("只输出 JSON", composition.Messages[0].Content);
        Assert.Contains("\"phases\"", composition.Messages[0].Content);
        Assert.Contains("read_file", composition.Messages[1].Content);
        Assert.True(composition.EstimatedTokens > 0);
    }

    [Fact]
    public void Compose_ExecutionPromptWrapsExistingSystemPrompt()
    {
        var composition = new AgentPromptComposer().Compose(new AgentPromptComposeRequest
        {
            Profile = AgentPromptProfile.Execution,
            Goal = "fix bug",
            SystemContext = new SystemPromptContext
            {
                ProjectName = "Demo",
                ProjectPath = @"D:\Code\Demo",
                EnabledToolIds = ["read_file"],
                ToolPermissionModes = new Dictionary<string, ToolPermissionMode>(StringComparer.OrdinalIgnoreCase)
                {
                    ["read_file"] = ToolPermissionMode.AutoReadOnly
                }
            }
        });

        var system = Assert.Single(composition.Messages);
        Assert.Equal(ChatRole.System, system.Role);
        Assert.Contains("你是 AIChat 的项目 Agent", system.Content);
        Assert.Contains("名称：Demo", system.Content);
        Assert.Contains("Prompt profile: Execution", system.Content);
        Assert.Contains("Goal: fix bug", system.Content);
    }

    [Fact]
    public void Compose_VerificationRepairPromptIncludesFailureSummaryAndPlan()
    {
        var composition = new AgentPromptComposer().Compose(new AgentPromptComposeRequest
        {
            Profile = AgentPromptProfile.VerificationRepair,
            Goal = "make tests pass",
            FailureSummary = "dotnet test failed",
            AllowedTools = ["read_file", "apply_patch", "run_test"],
            Plan = new AgentStructuredPlan
            {
                Summary = "repair tests",
                Phases =
                [
                    new AgentPlanPhase { Name = "repairing", Objective = "fix failing test" }
                ]
            }
        });

        Assert.Equal(2, composition.Messages.Count);
        Assert.Contains("验证修复 Agent", composition.Messages[0].Content);
        Assert.Contains("Plan: repair tests", composition.Messages[0].Content);
        Assert.Contains("run_test", composition.Messages[0].Content);
        Assert.Contains("dotnet test failed", composition.Messages[1].Content);
    }

    [Fact]
    public void Compose_PlanningPromptIncludesInputArtifactRefs()
    {
        var composition = new AgentPromptComposer().Compose(new AgentPromptComposeRequest
        {
            Profile = AgentPromptProfile.Planning,
            Goal = "use attached image",
            InputArtifactRefs = ["input-artifact:abc [Image] ui.png: login form screenshot"]
        });

        Assert.Contains("输入 artifact 引用", composition.Messages[1].Content);
        Assert.Contains("input-artifact:abc", composition.Messages[1].Content);
    }

    [Fact]
    public void Compose_ExecutionPromptIncludesInputArtifactRefs()
    {
        var composition = new AgentPromptComposer().Compose(new AgentPromptComposeRequest
        {
            Profile = AgentPromptProfile.Execution,
            Goal = "summarize upload",
            SystemContext = new SystemPromptContext
            {
                ProjectName = "Demo",
                InputArtifactRefs = ["input-artifact:def [Document] spec.pdf: API notes"]
            },
            InputArtifactRefs = ["input-artifact:def [Document] spec.pdf: API notes"]
        });

        Assert.Contains("输入 artifact：", composition.Messages[0].Content);
        Assert.Contains("Input artifact refs:", composition.Messages[0].Content);
        Assert.Contains("input-artifact:def", composition.Messages[0].Content);
    }
}
