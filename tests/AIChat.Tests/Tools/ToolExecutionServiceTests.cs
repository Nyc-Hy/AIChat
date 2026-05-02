using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Tools;

public sealed class ToolExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_AutoExecutesReadOnlyTool()
    {
        var tool = new FakeTool("read_file", AgentToolRisk.ReadOnly);
        var service = new ToolExecutionService(new AgentToolCatalog([tool]));

        var events = await CollectAsync(service.ExecuteAsync(new ToolExecutionRequest
        {
            ToolCall = new ChatToolCall { Name = "read_file", ArgumentsJson = "{}" },
            ProjectPath = Environment.CurrentDirectory
        }));

        var result = Assert.Single(events);
        Assert.Equal(ToolExecutionEventType.Result, result.Type);
        Assert.False(result.Result!.IsError);
        Assert.True(tool.WasExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_AsksApprovalForWriteTool()
    {
        var tool = new FakeTool("write_file", AgentToolRisk.Write);
        var service = new ToolExecutionService(new AgentToolCatalog([tool]));

        var events = await CollectAsync(service.ExecuteAsync(new ToolExecutionRequest
        {
            ToolCall = new ChatToolCall { Name = "write_file", ArgumentsJson = "{}" },
            ProjectPath = Environment.CurrentDirectory,
            RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve())
        }));

        Assert.Equal([ToolExecutionEventType.ApprovalRequired, ToolExecutionEventType.Result], events.Select(item => item.Type));
        Assert.True(tool.WasExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsRejectedResultWhenApprovalRejected()
    {
        var tool = new FakeTool("write_file", AgentToolRisk.Write);
        var service = new ToolExecutionService(new AgentToolCatalog([tool]));

        var events = await CollectAsync(service.ExecuteAsync(new ToolExecutionRequest
        {
            ToolCall = new ChatToolCall { Name = "write_file", ArgumentsJson = "{}" },
            ProjectPath = Environment.CurrentDirectory,
            RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Reject("no"))
        }));

        Assert.Equal(
            [ToolExecutionEventType.ApprovalRequired, ToolExecutionEventType.ApprovalRejected, ToolExecutionEventType.Result],
            events.Select(item => item.Type));
        Assert.True(events.Last().Result!.IsError);
        Assert.False(tool.WasExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotExecuteDisabledTool()
    {
        var tool = new FakeTool("write_file", AgentToolRisk.Write);
        var service = new ToolExecutionService(new AgentToolCatalog([tool]));

        var events = await CollectAsync(service.ExecuteAsync(new ToolExecutionRequest
        {
            ToolCall = new ChatToolCall { Name = "write_file", ArgumentsJson = "{}" },
            ProjectPath = Environment.CurrentDirectory,
            ToolPermissionModes = new Dictionary<string, ToolPermissionMode>
            {
                ["write_file"] = ToolPermissionMode.Disabled
            }
        }));

        var result = Assert.Single(events);
        Assert.True(result.Result!.IsError);
        Assert.Contains("关闭", result.Result.Content);
        Assert.False(tool.WasExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsSessionAllowedAndExecutesTool()
    {
        var tool = new FakeTool("write_file", AgentToolRisk.Write);
        var service = new ToolExecutionService(new AgentToolCatalog([tool]));

        var events = await CollectAsync(service.ExecuteAsync(new ToolExecutionRequest
        {
            ToolCall = new ChatToolCall { Name = "write_file", ArgumentsJson = "{}" },
            ProjectPath = Environment.CurrentDirectory,
            ToolPermissionModes = new Dictionary<string, ToolPermissionMode>
            {
                ["write_file"] = ToolPermissionMode.AllowForSession
            },
            RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve(allowForSession: true))
        }));

        Assert.Equal(
            [ToolExecutionEventType.ApprovalRequired, ToolExecutionEventType.SessionAllowed, ToolExecutionEventType.Result],
            events.Select(item => item.Type));
        Assert.Equal("write_file", events[1].SessionAllowedToolId);
        Assert.True(tool.WasExecuted);
    }

    [Fact]
    public async Task ExecuteAsync_SessionAllowFromConfirmEachTimeSkipsApprovalOnNextCall()
    {
        var tool = new FakeTool("write_file", AgentToolRisk.Write);
        var service = new ToolExecutionService(new AgentToolCatalog([tool]));

        // 第一次调用：ConfirmEachTime（默认），用户点"本会话允许"
        var firstEvents = await CollectAsync(service.ExecuteAsync(new ToolExecutionRequest
        {
            ToolCall = new ChatToolCall { Name = "write_file", ArgumentsJson = "{}" },
            ProjectPath = Environment.CurrentDirectory,
            RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve(allowForSession: true))
        }));

        Assert.Equal(
            [ToolExecutionEventType.ApprovalRequired, ToolExecutionEventType.SessionAllowed, ToolExecutionEventType.Result],
            firstEvents.Select(item => item.Type));

        var sessionAllowedTools = new HashSet<string> { "write_file" };

        // 第二次调用：同样的 ConfirmEachTime 模式，但已 session allow，应自动执行
        var secondEvents = await CollectAsync(service.ExecuteAsync(new ToolExecutionRequest
        {
            ToolCall = new ChatToolCall { Name = "write_file", ArgumentsJson = "{}" },
            ProjectPath = Environment.CurrentDirectory,
            SessionAllowedToolIds = sessionAllowedTools
        }));

        var secondResult = Assert.Single(secondEvents);
        Assert.Equal(ToolExecutionEventType.Result, secondResult.Type);
        Assert.True(tool.WasExecuted);
    }

    private static async Task<List<ToolExecutionEvent>> CollectAsync(IAsyncEnumerable<ToolExecutionEvent> events)
    {
        var result = new List<ToolExecutionEvent>();
        await foreach (var item in events)
        {
            result.Add(item);
        }

        return result;
    }

    private sealed class FakeTool : IAgentTool
    {
        private readonly AgentToolResult _result;

        public FakeTool(string id, AgentToolRisk risk)
        {
            Id = id;
            Risk = risk;
            _result = new AgentToolResult { ToolName = id, Content = "ok" };
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
                Summary = "preview"
            });
        }

        public Task<AgentToolResult> ExecuteAsync(string argumentsJson, AgentToolContext context, CancellationToken cancellationToken = default)
        {
            WasExecuted = true;
            return Task.FromResult(_result);
        }
    }
}
