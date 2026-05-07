using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Agents.Templates;
using AIChat.Application.Prompting;

namespace AIChat.Tests.Agents.Templates;

public sealed class AgentTemplateCatalogTests
{
    [Fact]
    public void CreateDefaults_ContainsExpectedTemplates()
    {
        var catalog = new AgentTemplateCatalog();

        Assert.Equal(
            ["planner", "explorer", "worker", "verifier", "summarizer", "reviewer"],
            catalog.All.Select(template => template.Id));
        Assert.All(catalog.All, template => Assert.False(string.IsNullOrWhiteSpace(template.OutputSchema)));
    }

    [Fact]
    public void Explorer_IsReadOnlyAndUsesContextGatheringProfile()
    {
        var explorer = new AgentTemplateCatalog().Get("explorer");
        var modes = explorer.BuildPermissionModes();

        Assert.False(explorer.CanWrite);
        Assert.False(explorer.CanVerify);
        Assert.Equal(AgentPromptProfile.ContextGathering, explorer.PromptProfile);
        Assert.All(modes.Values, mode => Assert.Equal(ToolPermissionMode.AutoReadOnly, mode));
        Assert.Contains("read_file", explorer.DefaultToolIds);
        Assert.Contains("read_input_artifact", explorer.DefaultToolIds);
        Assert.DoesNotContain("apply_patch", explorer.DefaultToolIds);
    }

    [Fact]
    public void Worker_CanWriteButRequiresConfirmationForMutationTools()
    {
        var worker = new AgentTemplateCatalog().Get("worker");
        var modes = worker.BuildPermissionModes();

        Assert.True(worker.CanWrite);
        Assert.False(worker.CanVerify);
        Assert.Equal(AgentPromptProfile.Execution, worker.PromptProfile);
        Assert.Equal(ToolPermissionMode.AutoReadOnly, modes["read_file"]);
        Assert.Equal(ToolPermissionMode.AutoReadOnly, modes["read_input_artifact"]);
        Assert.Equal(ToolPermissionMode.ConfirmEachTime, modes["apply_patch"]);
        Assert.Equal(ToolPermissionMode.ConfirmEachTime, modes["write_file"]);
    }

    [Fact]
    public void Verifier_CanVerifyButCannotWrite()
    {
        var verifier = new AgentTemplateCatalog().Get("verifier");
        var modes = verifier.BuildPermissionModes();

        Assert.False(verifier.CanWrite);
        Assert.True(verifier.CanVerify);
        Assert.Equal(ToolPermissionMode.ConfirmEachTime, modes["run_test"]);
        Assert.DoesNotContain("apply_patch", verifier.DefaultToolIds);
    }

    [Theory]
    [InlineData(AgentRunPhase.Planning, false, "planner")]
    [InlineData(AgentRunPhase.GatheringContext, false, "explorer")]
    [InlineData(AgentRunPhase.Executing, false, "explorer")]
    [InlineData(AgentRunPhase.Executing, true, "worker")]
    [InlineData(AgentRunPhase.Verifying, false, "verifier")]
    [InlineData(AgentRunPhase.Repairing, false, "verifier")]
    [InlineData(AgentRunPhase.Repairing, true, "worker")]
    [InlineData(AgentRunPhase.Summarizing, false, "summarizer")]
    public void SelectForPhase_ReturnsExpectedTemplate(AgentRunPhase phase, bool requiresWrite, string expectedId)
    {
        var catalog = new AgentTemplateCatalog();

        var template = catalog.SelectForPhase(phase, requiresWrite);

        Assert.Equal(expectedId, template.Id);
    }
}
