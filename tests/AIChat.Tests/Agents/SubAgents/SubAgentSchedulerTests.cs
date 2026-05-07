using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Agents;
using AIChat.Application.Agents.SubAgents;
using AIChat.Application.Context;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents.SubAgents;

public sealed class SubAgentSchedulerTests
{
    [Fact]
    public async Task RunAsync_CompletesReadOnlyExplorerAndAttributesToolCalls()
    {
        var toolCall = new ChatToolCall
        {
            Id = "tool-1",
            Name = "read_file",
            ArgumentsJson = "{}"
        };
        var chat = new QueueChatCompletionService([
            [new ChatDelta { ToolCalls = [toolCall] }],
            [new ChatDelta { Content = "Found the relevant service." }]
        ]);
        var scheduler = CreateScheduler(chat, [new FakeTool("read_file", AgentToolRisk.ReadOnly, "file content")]);

        var run = await scheduler.RunAsync(CreateRequest());

        Assert.Equal(SubAgentStatus.Completed, run.Status);
        Assert.NotNull(run.Result);
        Assert.Contains("Found the relevant service", run.Result!.Summary);
        var call = Assert.Single(run.ToolCalls);
        Assert.Equal("parent-1", call.ParentRunId);
        Assert.Equal(run.Id, call.SubAgentRunId);
        Assert.Equal("read_file", call.ToolName);
        Assert.False(call.IsError);
    }

    [Fact]
    public async Task RunAsync_RejectsDuplicateUnresolvedTask()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new BlockingChatCompletionService(gate.Task);
        var scheduler = CreateScheduler(chat, []);
        using var cts = new CancellationTokenSource();

        var first = scheduler.RunAsync(CreateRequest(task: "inspect auth"), cts.Token);
        await chat.Started.Task;

        var duplicate = await scheduler.RunAsync(CreateRequest(task: "inspect auth"));

        Assert.Equal(SubAgentStatus.Rejected, duplicate.Status);
        Assert.Contains("Duplicate", duplicate.Result!.Summary);
        await cts.CancelAsync();
        await first;
    }

    [Fact]
    public async Task RunAsync_FailsReadOnlyExplorerBeforeForbiddenMutationExecutes()
    {
        var forbiddenCall = new ChatToolCall
        {
            Id = "tool-1",
            Name = "apply_patch",
            ArgumentsJson = "{}"
        };
        var forbiddenTool = new FakeTool("apply_patch", AgentToolRisk.Write, "patched");
        var scheduler = CreateScheduler(
            new QueueChatCompletionService([[new ChatDelta { ToolCalls = [forbiddenCall] }]]),
            [forbiddenTool]);

        var run = await scheduler.RunAsync(CreateRequest());

        Assert.Equal(SubAgentStatus.Failed, run.Status);
        Assert.Contains("forbidden tool", run.Result!.Summary);
        Assert.False(forbiddenTool.WasExecuted);
    }

    [Fact]
    public async Task RunAsync_StopsWhenToolBudgetIsExceeded()
    {
        var first = new ChatToolCall { Id = "tool-1", Name = "read_file", ArgumentsJson = "{}" };
        var second = new ChatToolCall { Id = "tool-2", Name = "read_file", ArgumentsJson = "{}" };
        var scheduler = CreateScheduler(
            new QueueChatCompletionService([
                [new ChatDelta { ToolCalls = [first, second] }],
                [new ChatDelta { Content = "done" }]
            ]),
            [new FakeTool("read_file", AgentToolRisk.ReadOnly, "ok")]);

        var run = await scheduler.RunAsync(CreateRequest(maxToolCalls: 1));

        Assert.Equal(SubAgentStatus.BudgetExceeded, run.Status);
        Assert.Equal(1, run.ToolCallCount);
        Assert.Single(run.ToolCalls);
    }

    [Fact]
    public async Task RunAsync_RejectsWriteScopeForExplorer()
    {
        var scheduler = CreateScheduler(new QueueChatCompletionService([]), []);

        var run = await scheduler.RunAsync(CreateRequest(writeScope: ["src/App.cs"]));

        Assert.Equal(SubAgentStatus.Rejected, run.Status);
        Assert.Contains("write scope", run.Result!.Summary);
    }

    private static SubAgentScheduler CreateScheduler(IChatCompletionService chat, IReadOnlyList<IAgentTool> tools)
    {
        return new SubAgentScheduler(new AgentRunner(chat, new AgentToolCatalog(tools)));
    }

    private static SubAgentRunRequest CreateRequest(
        string task = "inspect auth",
        int maxToolCalls = 4,
        IReadOnlyList<string>? writeScope = null)
    {
        return new SubAgentRunRequest
        {
            ParentRunId = "parent-1",
            Task = task,
            ProjectPath = Environment.CurrentDirectory,
            Settings = new AppSettings { Model = "test" },
            ContextPack = new TaskContextPack
            {
                Summary = "Context pack",
                ArtifactRefs = ["run_test:tool_result:failed"]
            },
            MaxToolCalls = maxToolCalls,
            WriteScope = writeScope ?? []
        };
    }

    private sealed class QueueChatCompletionService : IChatCompletionService
    {
        private readonly Queue<IReadOnlyList<ChatDelta>> _responses;

        public QueueChatCompletionService(IEnumerable<IReadOnlyList<ChatDelta>> responses)
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

    private sealed class BlockingChatCompletionService : IChatCompletionService
    {
        private readonly Task _waitFor;

        public BlockingChatCompletionService(Task waitFor)
        {
            _waitFor = waitFor;
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ChatDelta> SendAsync(
            ChatRequest request,
            AppSettings settings,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await _waitFor.WaitAsync(cancellationToken);
            yield return new ChatDelta { Content = "done" };
        }
    }

    private sealed class FakeTool : IAgentTool
    {
        private readonly string _content;

        public FakeTool(string id, AgentToolRisk risk, string content)
        {
            Id = id;
            Risk = risk;
            _content = content;
            Definition = new ChatToolDefinition
            {
                Name = id,
                Description = id,
                ParametersJson = """{"type":"object"}"""
            };
        }

        public string Id { get; }
        public AgentToolRisk Risk { get; }
        public ChatToolDefinition Definition { get; }
        public bool WasExecuted { get; private set; }

        public Task<AgentToolPreview> PreviewAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentToolPreview
            {
                ToolName = Id,
                Risk = Risk,
                Summary = Id
            });
        }

        public Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
        {
            WasExecuted = true;
            return Task.FromResult(new AgentToolResult { ToolName = Id, Content = _content });
        }
    }
}
