using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Agents;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents;

public sealed class AgentHarnessTests
{
    [Fact]
    public async Task RunAsync_RecordsRunAndFinalStepForPlainResponse()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([new ChatDelta { Content = "done" }]),
            new AgentToolCatalog([])));

        var events = new List<AgentHarnessEvent>();
        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "do work",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "do work" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext { ProjectPath = Environment.CurrentDirectory }
                       }))
        {
            events.Add(item);
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal("user-1", run.UserMessageId);
        Assert.Contains(events, item => item.Type == AgentHarnessEventType.RunStarted);
        Assert.Contains(events, item => item.Type == AgentHarnessEventType.ContentDelta && item.Content == "done");
        Assert.Equal(2, run.Steps.Count);
        Assert.Equal(AgentStepType.Model, run.Steps[0].Type);
        Assert.Equal(AgentStepType.Final, run.Steps[1].Type);
        Assert.Equal("done", run.Steps[1].Output);
    }

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        private readonly IReadOnlyList<ChatDelta> _deltas;

        public FakeChatCompletionService(IReadOnlyList<ChatDelta> deltas)
        {
            _deltas = deltas;
        }

        public async IAsyncEnumerable<ChatDelta> SendAsync(
            ChatRequest request,
            AppSettings settings,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var delta in _deltas)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return delta;
                await Task.Yield();
            }
        }
    }
}
