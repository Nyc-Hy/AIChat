using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Agents;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;
using AIChat.Tests.Tools;

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

    [Fact]
    public async Task RunAsync_RecordsFileChangesForSuccessfulMutationTool()
    {
        using var workspace = TemporaryWorkspace.Create();
        var targetPath = Path.Combine(workspace.Path, "notes.txt");
        var secondTargetPath = Path.Combine(workspace.Path, "todo.txt");
        await File.WriteAllTextAsync(targetPath, "old value");
        await File.WriteAllTextAsync(secondTargetPath, "todo old");

        var conversation = new Conversation { Id = "conversation-1" };
        var toolCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "apply_patch",
            ArgumentsJson = """
            {
              "changes": [
                {
                  "path": "notes.txt",
                  "old_text": "old value",
                  "new_text": "new value"
                },
                {
                  "path": "todo.txt",
                  "old_text": "todo old",
                  "new_text": "todo new"
                }
              ]
            }
            """
        };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([
                [new ChatDelta { ToolCalls = [toolCall] }],
                [new ChatDelta { Content = "done" }]
            ]),
            new AgentToolCatalog([new ApplyPatchTool()])));

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "update notes",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "update notes" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = workspace.Path,
                               RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve())
                           }
                       }))
        {
        }

        Assert.Equal("new value", await File.ReadAllTextAsync(targetPath));
        Assert.Equal("todo new", await File.ReadAllTextAsync(secondTargetPath));
        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(2, run.FileChanges.Count);
        var fileChange = run.FileChanges.Single(change => change.Path == "notes.txt");
        Assert.Equal("notes.txt", fileChange.Path);
        Assert.Equal("apply_patch", fileChange.ToolName);
        Assert.Equal("tool-call-1", fileChange.ToolCallId);
        Assert.Equal("old value".Length, fileChange.OldChars);
        Assert.Equal("new value".Length, fileChange.NewChars);
        Assert.Contains("-old value", fileChange.DiffText);
        Assert.Contains("+new value", fileChange.DiffText);
        Assert.DoesNotContain("todo old", fileChange.DiffText);

        var secondFileChange = run.FileChanges.Single(change => change.Path == "todo.txt");
        Assert.Contains("-todo old", secondFileChange.DiffText);
        Assert.Contains("+todo new", secondFileChange.DiffText);
        Assert.DoesNotContain("old value", secondFileChange.DiffText);
    }

    private sealed class FakeChatCompletionService : IChatCompletionService
    {
        private readonly Queue<IReadOnlyList<ChatDelta>> _responses;

        public FakeChatCompletionService(IReadOnlyList<ChatDelta> deltas)
            : this([deltas])
        {
        }

        public FakeChatCompletionService(IEnumerable<IReadOnlyList<ChatDelta>> responses)
        {
            _responses = new Queue<IReadOnlyList<ChatDelta>>(responses);
        }

        public async IAsyncEnumerable<ChatDelta> SendAsync(
            ChatRequest request,
            AppSettings settings,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var deltas = _responses.Count > 0 ? _responses.Dequeue() : [];
            foreach (var delta in deltas)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return delta;
                await Task.Yield();
            }
        }
    }
}
