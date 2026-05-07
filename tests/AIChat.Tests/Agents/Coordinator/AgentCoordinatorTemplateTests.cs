using AIChat.Application.Agents.Coordinator;

namespace AIChat.Tests.Agents.Coordinator;

public sealed class AgentCoordinatorTemplateTests
{
    [Fact]
    public void SelectTemplate_DelegatesPhaseSelectionWithoutSpawning()
    {
        var coordinator = new AgentCoordinator();

        var explorer = coordinator.SelectTemplate(AgentRunPhase.GatheringContext);
        var worker = coordinator.SelectTemplate(AgentRunPhase.Executing, requiresWrite: true);

        Assert.Equal("explorer", explorer.Id);
        Assert.Equal("worker", worker.Id);
        Assert.False(explorer.CanWrite);
        Assert.True(worker.CanWrite);
    }
}
