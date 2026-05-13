using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Agents;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents;

public sealed class AgentRunnerTests
{
    [Fact]
    public async Task RunAsync_FeedsSummarizedToolContentBackToModel()
    {
        var content = string.Join('\n', Enumerable.Range(1, 600).Select(i => $"line {i:000}"));
        var toolCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "read_file",
            ArgumentsJson = "{}"
        };
        var chatService = new RecordingChatCompletionService([
            [new ChatDelta { ToolCalls = [toolCall] }],
            [new ChatDelta { Content = "done" }]
        ]);
        var runner = new AgentRunner(
            chatService,
            new AgentToolCatalog([new LargeReadOnlyTool(content)]));

        await foreach (var _ in runner.RunAsync(
                           new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "inspect files" }]
                           },
                           new AppSettings { Model = "test" },
                           new AgentRunContext { ProjectPath = Environment.CurrentDirectory }))
        {
        }

        Assert.Equal(2, chatService.Requests.Count);
        var toolMessage = chatService.Requests[1].Messages.Single(message => message.Role == ChatRole.Tool);
        Assert.NotEqual(content, toolMessage.Content);
        Assert.Contains("原文已保存为运行产物", toolMessage.Content);
    }

    [Fact]
    public async Task RunAsync_EmitsBudgetExceededBeforeExecutingPastToolLimit()
    {
        var first = new ChatToolCall { Id = "tool-1", Name = "read_file", ArgumentsJson = "{}" };
        var second = new ChatToolCall { Id = "tool-2", Name = "read_file", ArgumentsJson = "{}" };
        var tool = new LargeReadOnlyTool("ok");
        var runner = new AgentRunner(
            new RecordingChatCompletionService([
                [new ChatDelta { ToolCalls = [first, second] }]
            ]),
            new AgentToolCatalog([tool]));

        var events = new List<AgentRunEvent>();
        await foreach (var item in runner.RunAsync(
                           new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "inspect files" }]
                           },
                           new AppSettings { Model = "test" },
                           new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               MaxToolRounds = 1
                           }))
        {
            events.Add(item);
        }

        Assert.Contains(events, item => item.Type == AgentRunEventType.BudgetExceeded);
        Assert.Equal(1, tool.ExecuteCount);
        Assert.Single(events, item => item.Type == AgentRunEventType.Completed);
    }

    [Fact]
    public async Task RunAsync_DoesNotRetryPlainAnswerBecauseGoalContainsMutationWords()
    {
        var chatService = new RecordingChatCompletionService([
            [new ChatDelta { Content = "done" }]
        ]);
        var runner = new AgentRunner(
            chatService,
            new AgentToolCatalog([new LargeReadOnlyTool("ok")]));

        var events = new List<AgentRunEvent>();
        await foreach (var item in runner.RunAsync(
                           new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "修改项目里的说明文档" }]
                           },
                           new AppSettings { Model = "test" },
                           new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"]
                           }))
        {
            events.Add(item);
        }

        Assert.Single(chatService.Requests);
        Assert.Contains(events, item => item.Type == AgentRunEventType.ContentDelta && item.Content == "done");
        Assert.Single(events, item => item.Type == AgentRunEventType.Completed);
    }

    private sealed class RecordingChatCompletionService : IChatCompletionService
    {
        private readonly Queue<IReadOnlyList<ChatDelta>> _responses;

        public RecordingChatCompletionService(IEnumerable<IReadOnlyList<ChatDelta>> responses)
        {
            _responses = new Queue<IReadOnlyList<ChatDelta>>(responses);
        }

        public List<ChatRequest> Requests { get; } = [];

        public async IAsyncEnumerable<ChatDelta> SendAsync(
            ChatRequest request,
            AppSettings settings,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var deltas = _responses.Count > 0 ? _responses.Dequeue() : [];
            foreach (var delta in deltas)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return delta;
                await Task.Yield();
            }
        }
    }

    private sealed class LargeReadOnlyTool : IAgentTool
    {
        private readonly string _content;

        public LargeReadOnlyTool(string content)
        {
            _content = content;
        }

        public string Id => "read_file";
        public AgentToolRisk Risk => AgentToolRisk.ReadOnly;
        public ChatToolDefinition Definition { get; } = new()
        {
            Name = "read_file",
            Description = "read",
            ParametersJson = """{"type":"object"}"""
        };

        public Task<AgentToolPreview> PreviewAsync(
            string argumentsJson,
            AgentToolContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentToolPreview
            {
                ToolName = Id,
                Risk = Risk,
                Summary = "read"
            });
        }

        public Task<AgentToolResult> ExecuteAsync(
            string argumentsJson,
            AgentToolContext context,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult(new AgentToolResult
            {
                ToolName = Id,
                Content = _content
            });
        }

        public int ExecuteCount { get; private set; }
    }
}
