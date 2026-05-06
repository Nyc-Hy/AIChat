using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Agents.Planning;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents.Planning;

public sealed class AgentPlannerTests
{
    [Fact]
    public async Task PlanAsync_ReturnsParsedStructuredPlan()
    {
        var service = new FakeChatCompletionService("""
        {
          "summary": "Read then patch",
          "phases": [
            {
              "name": "gathering_context",
              "tasks": [
                { "title": "Read files", "risk": "low", "suggestedTools": ["read_file"] }
              ]
            },
            {
              "name": "executing",
              "tasks": [
                { "title": "Patch code", "risk": "medium", "suggestedTools": ["apply_patch"] }
              ]
            }
          ]
        }
        """);
        var planner = new AgentPlanner(service);

        var plan = await planner.PlanAsync(CreateRequest(), new AppSettings { Model = "test", Temperature = 0.7 });

        Assert.False(plan.IsFallback);
        Assert.Equal("Read then patch", plan.Summary);
        Assert.Equal(2, plan.Phases.Count);
        Assert.Equal("gathering_context", plan.Phases[0].Name);
        Assert.Equal("Patch code", plan.Phases[1].Tasks[0].Title);
        Assert.Single(service.Requests);
        Assert.Empty(service.Requests[0].Tools);
    }

    [Fact]
    public async Task PlanAsync_FallsBackWhenModelReturnsInvalidJson()
    {
        var planner = new AgentPlanner(new FakeChatCompletionService("not-json"));

        var plan = await planner.PlanAsync(CreateRequest(), new AppSettings { Model = "test" });

        Assert.True(plan.IsFallback);
        Assert.Single(plan.Phases);
        Assert.Single(plan.Phases[0].Tasks);
    }

    private static AgentPlanningRequest CreateRequest()
    {
        return new AgentPlanningRequest(
            "Modify project",
            Environment.CurrentDirectory,
            ["read_file", "apply_patch"],
            [new ChatMessage { Role = ChatRole.User, Content = "Modify project" }]);
    }

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        private readonly string _content;

        public FakeChatCompletionService(string content)
        {
            _content = content;
        }

        public List<ChatRequest> Requests { get; } = [];

        public async IAsyncEnumerable<ChatDelta> SendAsync(
            ChatRequest request,
            AppSettings settings,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            yield return new ChatDelta { Content = _content };
            await Task.Yield();
        }
    }
}
