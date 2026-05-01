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
                           WorkspaceBranch = "## main",
                           WorkspaceChangeCountAtStart = 2,
                           WorkspaceChangesWereTruncated = true,
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               ToolPermissionModes = new Dictionary<string, ToolPermissionMode>
                               {
                                   ["read_file"] = ToolPermissionMode.AutoReadOnly
                               }
                           }
                       }))
        {
            events.Add(item);
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal("user-1", run.UserMessageId);
        Assert.Equal(Environment.CurrentDirectory, run.ProjectPath);
        Assert.Equal("test", run.Model);
        Assert.Equal(["read_file"], run.EnabledTools);
        Assert.Equal("AutoReadOnly", run.ToolPermissionModes["read_file"]);
        Assert.Equal("## main", run.WorkspaceBranch);
        Assert.Equal(2, run.WorkspaceChangeCountAtStart);
        Assert.True(run.WorkspaceChangesWereTruncated);
        Assert.Equal(4, run.MaxToolRounds);
        Assert.Contains(events, item => item.Type == AgentHarnessEventType.RunStarted);
        Assert.Contains(events, item => item.Type == AgentHarnessEventType.ContentDelta && item.Content == "done");
        Assert.Equal(2, run.Steps.Count);
        Assert.Equal(AgentStepType.Model, run.Steps[0].Type);
        Assert.Contains("模型：test", run.Steps[0].Output);
        Assert.Contains("预算：最多 4 轮工具调用", run.Steps[0].Output);
        Assert.Contains("工作区：## main · 2 个启动变更", run.Steps[0].Output);
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

    [Fact]
    public async Task RunAsync_RecordsVerificationResultsForBuildAndTestTools()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var toolCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "run_test",
            ArgumentsJson = "{}"
        };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([
                [new ChatDelta { ToolCalls = [toolCall] }],
                [new ChatDelta { Content = "done" }]
            ]),
            new AgentToolCatalog([new FakeVerificationTool()])));

        var toolCallPhases = new List<string>();
        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "verify",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "verify" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve())
                           }
                       }))
        {
            if (item.Type == AgentHarnessEventType.ToolCall && item.Run is not null)
            {
                toolCallPhases.Add(item.Run.Phase);
            }
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Contains("verifying", toolCallPhases);
        Assert.Equal("completed", run.Phase);
        var verification = Assert.Single(run.Verifications);
        Assert.Equal("run_test", verification.ToolName);
        Assert.Equal("dotnet test", verification.Command);
        Assert.Equal(0, verification.ExitCode);
        Assert.True(verification.IsSuccess);
        Assert.Contains("Passed", verification.Output);
    }

    [Fact]
    public async Task RunAsync_RecordsBudgetExhaustionWhenToolRoundsAreExceeded()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var toolCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "run_test",
            ArgumentsJson = "{}"
        };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([
                [new ChatDelta { ToolCalls = [toolCall] }]
            ]),
            new AgentToolCatalog([new FakeVerificationTool()])));

        var content = "";
        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "verify",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "verify" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               MaxToolRounds = 1,
                               RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve())
                           }
                       }))
        {
            if (item.Type == AgentHarnessEventType.ContentDelta)
            {
                content += item.Content;
            }
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(1, run.MaxToolRounds);
        Assert.Equal(1, run.ToolCallCount);
        Assert.True(run.ToolBudgetExceeded);
        Assert.Equal("已达到工具调用轮数上限。", run.CompletionReason);
        Assert.Contains("必要时把工具轮数预算提高到 3", run.RecoverySuggestion);
        Assert.Contains("已达到工具调用轮数上限", content);
    }

    [Fact]
    public async Task RunAsync_FlagsMutationGoalWithoutSuccessfulMutationTool()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([new ChatDelta { Content = "done" }]),
            new AgentToolCatalog([])));

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "修改项目里的说明文档",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "修改项目里的说明文档" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext { ProjectPath = Environment.CurrentDirectory }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.True(run.RequiresProjectMutation);
        Assert.False(run.MutationToolSucceeded);
        Assert.Equal("任务看起来需要修改项目，但本轮没有记录到成功的修改工具。", run.CompletionReason);
        Assert.Contains("项目修改：未记录修改工具", run.FinalValidationSummary);
        Assert.Contains("实际调用写入或编辑工具", run.RecoverySuggestion);
    }

    [Fact]
    public async Task RunAsync_RecordsApprovalGuardrailsAndFinalValidation()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var toolCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "run_test",
            ArgumentsJson = "{}"
        };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([
                [new ChatDelta { ToolCalls = [toolCall] }],
                [new ChatDelta { Content = "done" }]
            ]),
            new AgentToolCatalog([new FakeVerificationTool()])));

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "verify",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "verify" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               ToolPermissionModes = new Dictionary<string, ToolPermissionMode>
                               {
                                   ["run_test"] = ToolPermissionMode.AllowForSession
                               },
                               RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve(allowForSession: true))
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(1, run.ToolApprovalRequiredCount);
        Assert.Equal(0, run.ToolApprovalRejectedCount);
        Assert.Equal(1, run.ToolSessionAllowedCount);
        Assert.Contains("工具审批：无拒绝", run.FinalValidationSummary);
        Assert.Contains("验证：1/1 通过", run.FinalValidationSummary);
        Assert.Contains("复查并继续", run.RecoverySuggestion);
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

    private sealed class FakeVerificationTool : IAgentTool
    {
        public string Id => "run_test";
        public AgentToolRisk Risk => AgentToolRisk.Shell;
        public ChatToolDefinition Definition { get; } = new()
        {
            Name = "run_test",
            Description = "fake test",
            ParametersJson = "{}"
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
                Summary = "run fake tests",
                PreviewText = "dotnet test"
            });
        }

        public Task<AgentToolResult> ExecuteAsync(
            string argumentsJson,
            AgentToolContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentToolResult
            {
                ToolName = Id,
                Content = """
                {
                  "command": "dotnet test",
                  "exitCode": 0,
                  "timedOut": false,
                  "output": "Passed"
                }
                """
            });
        }
    }
}
