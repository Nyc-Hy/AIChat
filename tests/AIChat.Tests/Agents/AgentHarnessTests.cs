using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Agents;
using AIChat.Application.Agents.Planning;
using AIChat.Application.Agents.SubAgents;
using AIChat.Application.Context;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
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
        Assert.Equal("Standard", run.TaskComplexity);
        Assert.Contains("planner=True", run.ExecutionPolicySummary);
        Assert.False(run.PlannerUsed);
        Assert.False(run.ExplorerUsed);
        Assert.Equal("Completion evidence satisfied.", run.FinalStatusReason);
        Assert.Equal("user-1", run.UserMessageId);
        Assert.Equal(Environment.CurrentDirectory, run.ProjectPath);
        Assert.Equal("test", run.Model);
        Assert.Equal(["read_file"], run.EnabledTools);
        Assert.Equal("AutoReadOnly", run.ToolPermissionModes["read_file"]);
        Assert.Equal("## main", run.WorkspaceBranch);
        Assert.Equal(2, run.WorkspaceChangeCountAtStart);
        Assert.True(run.WorkspaceChangesWereTruncated);
        Assert.Equal(4, run.MaxToolRounds);
        Assert.Equal(1, run.ModelCallCount);
        Assert.Equal(0, run.ContextEstimatedTokens);
        Assert.Equal(0, run.ContextRefCount);
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
    public async Task RunAsync_StartsWithStructuredPlanWhenPlannerIsConfigured()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var runnerService = new FakeChatCompletionService([new ChatDelta { Content = "done" }]);
        var plannerService = new FakeChatCompletionService([new ChatDelta
        {
            Content = """
            {
              "summary": "Plan first",
              "phases": [
                {
                  "name": "gathering_context",
                  "tasks": [
                    { "title": "Read context", "risk": "low", "suggestedTools": ["read_file"] }
                  ]
                }
              ]
            }
            """
        }]);
        var harness = new AgentHarness(
            new AgentRunner(runnerService, new AgentToolCatalog([])),
            new AgentPlanner(plannerService));

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
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               MaxToolRounds = 50
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.NotNull(run.StructuredPlan);
        Assert.Equal(2, run.ModelCallCount);
        Assert.Equal("Plan first", run.StructuredPlan!.Summary);
        Assert.NotNull(run.Plan);
        Assert.Contains(run.Steps, step => step.Title == "生成结构化计划");
        Assert.Contains(run.Plan!.Items, item => item.Title.Contains("Read context", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_SkipsPlannerForSimpleReadOnlyTask()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var runnerService = new FakeChatCompletionService([new ChatDelta { Content = "done" }]);
        var plannerService = new FakeChatCompletionService([new ChatDelta
        {
            Content = """{"summary":"should not be used","phases":[]}"""
        }]);
        var harness = new AgentHarness(
            new AgentRunner(runnerService, new AgentToolCatalog([])),
            new AgentPlanner(plannerService));

        var events = new List<AgentHarnessEvent>();
        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "解释这个项目的 Agent loop",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "解释这个项目的 Agent loop" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           ContextPack = new TaskContextPack
                           {
                               Summary = "Context pack: 1 files, ~123 tokens",
                               IncludedFiles = [new TaskContextFileRef { Path = "src/App.cs", Reason = "test", Score = 1 }],
                               EstimatedTokens = 123
                           },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               MaxToolRounds = 50
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Null(run.StructuredPlan);
        Assert.Null(run.Plan);
        Assert.Equal("Simple", run.TaskComplexity);
        Assert.Contains("mode=Fast Path", run.ExecutionPolicySummary);
        Assert.Contains("planner=False", run.ExecutionPolicySummary);
        Assert.False(run.PlannerUsed);
        Assert.False(run.ExplorerUsed);
        Assert.Equal(4, run.MaxToolRounds);
        Assert.Equal(1, run.ModelCallCount);
        Assert.Equal(123, run.ContextEstimatedTokens);
        Assert.True(run.ContextRefCount >= 2);
        Assert.Contains("Explorer skipped", run.ExplorerDecisionReason);
        Assert.Empty(plannerService.Requests);
        Assert.Single(runnerService.Requests);
    }

    [Fact]
    public async Task RunAsync_SkipsExplorerForStandardTaskWhenContextIsAvailable()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var runnerService = new FakeChatCompletionService([new ChatDelta { Content = "done" }]);
        var plannerService = new FakeChatCompletionService([new ChatDelta
        {
            Content = """
            {
              "summary": "Use existing context",
              "phases": [
                {
                  "name": "gathering_context",
                  "tasks": [
                    { "title": "Inspect existing file", "suggestedTools": ["read_file"] }
                  ]
                }
              ]
            }
            """
        }]);
        var subAgentService = new FakeChatCompletionService([new ChatDelta { Content = "should not run" }]);
        var subAgentScheduler = new SubAgentScheduler(new AgentRunner(subAgentService, new AgentToolCatalog([])));
        var harness = new AgentHarness(
            new AgentRunner(runnerService, new AgentToolCatalog([])),
            new AgentPlanner(plannerService),
            subAgentScheduler: subAgentScheduler);

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "fix login",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "fix login" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           ContextPack = new TaskContextPack
                           {
                               IncludedFiles = [new TaskContextFileRef { Path = "src/Login.cs", Reason = "goal match" }]
                           },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               MaxToolRounds = 50
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal("Standard", run.TaskComplexity);
        Assert.True(run.PlannerUsed);
        Assert.False(run.ExplorerUsed);
        Assert.Contains("mode=Standard Agent Loop", run.ExecutionPolicySummary);
        Assert.Equal(24, run.MaxToolRounds);
        Assert.Contains("Explorer skipped", run.ExplorerDecisionReason);
        Assert.Empty(run.SubAgentRuns);
        Assert.Empty(subAgentService.Requests);
    }

    [Fact]
    public async Task RunAsync_AppliesHistoricalBudgetAdjustment()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        conversation.AgentRuns.Add(new AgentRun
        {
            Id = "old-1",
            TaskComplexity = "Standard",
            ToolBudgetExceeded = true,
            Status = AgentRunStatus.BudgetExceeded,
            StartedAt = DateTimeOffset.Now.AddMinutes(-2)
        });
        conversation.AgentRuns.Add(new AgentRun
        {
            Id = "old-2",
            TaskComplexity = "Standard",
            ToolBudgetExceeded = true,
            Status = AgentRunStatus.BudgetExceeded,
            StartedAt = DateTimeOffset.Now.AddMinutes(-1)
        });
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([new ChatDelta { Content = "done" }]),
            new AgentToolCatalog([])));

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "fix login",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "fix login" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               MaxToolRounds = 40
                           }
                       }))
        {
        }

        var run = conversation.AgentRuns.Last();
        Assert.Equal(30, run.MaxToolRounds);
        Assert.Contains("strategy=recent budget pressure", run.ExecutionPolicySummary);
    }

    [Fact]
    public async Task RunAsync_SkipsPlannerWhenContinuingPausedRun()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var runnerService = new FakeChatCompletionService([new ChatDelta { Content = "continued" }]);
        var plannerService = new FakeChatCompletionService([new ChatDelta
        {
            Content = """{"summary":"should not plan continuation","phases":[]}"""
        }]);
        var harness = new AgentHarness(
            new AgentRunner(runnerService, new AgentToolCatalog([])),
            new AgentPlanner(plannerService));

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "继续完成这个已暂停的 Agent 任务。原始目标：fix login",
                           ContinuedFromRunId = "previous-run",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "continue" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               MaxToolRounds = 50
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal("previous-run", run.ContinuedFromRunId);
        Assert.False(run.PlannerUsed);
        Assert.Contains("mode=Continuation", run.ExecutionPolicySummary);
        Assert.Contains("planner=False", run.ExecutionPolicySummary);
        Assert.Equal(24, run.MaxToolRounds);
        Assert.Empty(plannerService.Requests);
    }

    [Fact]
    public async Task RunAsync_RecordsRetriedFromRunId()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var runnerService = new FakeChatCompletionService([new ChatDelta { Content = "retried" }]);
        var harness = new AgentHarness(new AgentRunner(runnerService, new AgentToolCatalog([])));

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "retry failed task",
                           RetriedFromRunId = "failed-run",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "retry" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = [],
                               MaxToolRounds = 50
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal("failed-run", run.RetriedFromRunId);
    }

    [Fact]
    public async Task RunAsync_RunsExplorerSubAgentAndRecordsResultArtifact()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var runnerService = new FakeChatCompletionService([new ChatDelta { Content = "done" }]);
        var plannerService = new FakeChatCompletionService([new ChatDelta
        {
            Content = """
            {
              "summary": "Explore first",
              "phases": [
                {
                  "name": "gathering_context",
                  "objective": "inspect files",
                  "tasks": [
                    { "title": "Inspect service", "details": "Read the relevant service", "risk": "low", "suggestedTools": ["read_file"] }
                  ]
                },
                { "name": "executing", "objective": "finish task" }
              ]
            }
            """
        }]);
        var subAgentService = new FakeChatCompletionService([new ChatDelta { Content = "Explorer found src/App.cs." }]);
        var subAgentScheduler = new SubAgentScheduler(new AgentRunner(subAgentService, new AgentToolCatalog([])));
        var harness = new AgentHarness(
            new AgentRunner(runnerService, new AgentToolCatalog([])),
            new AgentPlanner(plannerService),
            subAgentScheduler: subAgentScheduler);

        var events = new List<AgentHarnessEvent>();
        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "complex multi-module fix app",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "complex multi-module fix app" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           ContextPack = new TaskContextPack
                           {
                               Summary = "Context pack",
                               IncludedFiles = [new TaskContextFileRef { Path = "src/App.cs", Reason = "goal match" }]
                           },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               MaxToolRounds = 9
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        var subAgentRun = Assert.Single(run.SubAgentRuns);
        Assert.Equal("Complex", run.TaskComplexity);
        Assert.True(run.PlannerUsed);
        Assert.True(run.ExplorerUsed);
        Assert.Contains("Explorer scheduled", run.ExplorerDecisionReason);
        Assert.Equal("explorer", subAgentRun.TemplateId);
        Assert.Equal("Completed", subAgentRun.Status);
        Assert.Contains("Explorer found src/App.cs.", subAgentRun.Summary);
        Assert.Contains(run.Steps, step => step.Title == "Explorer 子 Agent" &&
                                          step.Output.Contains("Explorer found src/App.cs.", StringComparison.Ordinal));
        var artifact = Assert.Single(run.Artifacts, item => item.Kind == "sub_agent_result");
        Assert.Equal("explorer", artifact.Metadata["templateId"]);
        Assert.Contains("Explorer found src/App.cs.", artifact.Content);
        Assert.Contains(
            runnerService.Requests.Last().Messages,
            message => message.Role == ChatRole.System &&
                       message.Content.Contains("Explorer sub-agent result", StringComparison.Ordinal) &&
                       message.Content.Contains("Explorer found src/App.cs.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_RunsPlannerRequestedExplorerSubAgent()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var runnerService = new FakeChatCompletionService([new ChatDelta { Content = "done" }]);
        var plannerService = new FakeChatCompletionService([new ChatDelta
        {
            Content = """
            {
              "summary": "Explore requested area",
              "subAgents": [
                {
                  "templateId": "explorer",
                  "phase": "gathering_context",
                  "task": "Inspect repository routing code",
                  "reason": "The parent run needs focused context before editing.",
                  "maxToolCalls": 3
                }
              ],
              "phases": [
                {
                  "name": "executing",
                  "objective": "finish task",
                  "tasks": [
                    { "title": "Apply fix", "risk": "medium", "suggestedTools": ["read_file"] }
                  ]
                }
              ]
            }
            """
        }]);
        var subAgentService = new FakeChatCompletionService([new ChatDelta { Content = "Explorer completed planned task." }]);
        var subAgentScheduler = new SubAgentScheduler(new AgentRunner(subAgentService, new AgentToolCatalog([])));
        var harness = new AgentHarness(
            new AgentRunner(runnerService, new AgentToolCatalog([])),
            new AgentPlanner(plannerService),
            subAgentScheduler: subAgentScheduler);

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "complex multi-module fix routing",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "complex multi-module fix routing" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           ContextPack = new TaskContextPack
                           {
                               Summary = "Context pack",
                               IncludedFiles = [new TaskContextFileRef { Path = "src/Routing.cs", Reason = "goal match" }]
                           },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               MaxToolRounds = 9
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        var scheduleDecision = Assert.Single(run.SubAgentScheduleDecisions);
        Assert.Equal("Scheduled", scheduleDecision.Status);
        Assert.Single(run.StructuredPlan!.SubAgents);
        var subAgentRun = Assert.Single(run.SubAgentRuns);
        Assert.Equal("explorer", subAgentRun.TemplateId);
        Assert.Contains("Inspect repository routing code", subAgentRun.Task, StringComparison.Ordinal);
        Assert.Contains("The parent run needs focused context", subAgentRun.Task, StringComparison.Ordinal);
        Assert.Equal(3, subAgentRun.MaxToolCalls);
        Assert.Contains("Explorer completed planned task.", subAgentRun.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_RecordsFailedExplorerSubAgentAndContinuesParentRun()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var runnerService = new FakeChatCompletionService([new ChatDelta { Content = "main continued" }]);
        var plannerService = new FakeChatCompletionService([new ChatDelta
        {
            Content = """
            {
              "summary": "Explore first",
              "phases": [
                {
                  "name": "gathering_context",
                  "objective": "inspect files",
                  "tasks": [
                    { "title": "Inspect service", "details": "Read the relevant service", "risk": "low", "suggestedTools": ["read_file"] }
                  ]
                },
                { "name": "executing", "objective": "finish task" }
              ]
            }
            """
        }]);
        var forbiddenToolCall = new ChatToolCall
        {
            Id = "sub-agent-tool-1",
            Name = "apply_patch",
            ArgumentsJson = "{}"
        };
        var subAgentService = new FakeChatCompletionService([
            [new ChatDelta { ToolCalls = [forbiddenToolCall] }]
        ]);
        var subAgentScheduler = new SubAgentScheduler(new AgentRunner(subAgentService, new AgentToolCatalog([])));
        var harness = new AgentHarness(
            new AgentRunner(runnerService, new AgentToolCatalog([])),
            new AgentPlanner(plannerService),
            subAgentScheduler: subAgentScheduler);

        var events = new List<AgentHarnessEvent>();
        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "complex multi-module fix app",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "complex multi-module fix app" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           ContextPack = new TaskContextPack
                           {
                               Summary = "Context pack",
                               IncludedFiles = [new TaskContextFileRef { Path = "src/App.cs", Reason = "goal match" }]
                           },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               MaxToolRounds = 9
                           }
                       }))
        {
            events.Add(item);
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Contains(events, item => item.Type == AgentHarnessEventType.SubAgentCompleted &&
                                       item.SubAgentRun?.Status == "Failed");
        var subAgentRun = Assert.Single(run.SubAgentRuns);
        Assert.Equal("explorer", subAgentRun.TemplateId);
        Assert.Equal("Failed", subAgentRun.Status);
        Assert.Contains("forbidden tool: apply_patch", subAgentRun.Summary, StringComparison.Ordinal);
        Assert.Contains(run.Steps, step => step.Type == AgentStepType.Final &&
                                          step.Output.Contains("main continued", StringComparison.Ordinal));
        Assert.Contains(
            runnerService.Requests.Last().Messages,
            message => message.Role == ChatRole.System &&
                       message.Content.Contains("Explorer sub-agent result", StringComparison.Ordinal) &&
                       message.Content.Contains("forbidden tool: apply_patch", StringComparison.Ordinal));
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
                [new ChatDelta { Content = "已修改 notes.txt 和 todo.txt。" }]
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
        Assert.Contains("结果一致性：声明与工具记录一致", run.FinalValidationSummary);
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
                [new ChatDelta { Content = "已运行测试，全部通过。" }]
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
    public async Task RunAsync_RecordsSnapshotAndHashForFileChanges()
    {
        using var workspace = TemporaryWorkspace.Create();
        var targetPath = Path.Combine(workspace.Path, "config.txt");
        await File.WriteAllTextAsync(targetPath, "original content");

        var conversation = new Conversation { Id = "conversation-1" };
        var toolCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "apply_patch",
            ArgumentsJson = """
            {
              "changes": [
                {
                  "path": "config.txt",
                  "old_text": "original content",
                  "new_text": "updated content"
                }
              ]
            }
            """
        };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([
                [new ChatDelta { ToolCalls = [toolCall] }],
                [new ChatDelta { Content = "已运行测试，全部通过。" }]
            ]),
            new AgentToolCatalog([new ApplyPatchTool()])));

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "update config",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "update config" }]
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

        var run = Assert.Single(conversation.AgentRuns);
        var change = Assert.Single(run.FileChanges);
        Assert.Equal("original content", change.ContentSnapshot);
        Assert.False(string.IsNullOrEmpty(change.PostChangeHash));
    }

    [Fact]
    public async Task RunAsync_CompletesPlainResponseWithoutKeywordMutationFailure()
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
            events.Add(item);
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal("Completion evidence satisfied.", run.FinalStatusReason);
        Assert.False(run.RequiresProjectMutation);
        Assert.False(run.MutationToolSucceeded);
        Assert.Contains("项目修改：未记录修改工具", run.FinalValidationSummary);
        Assert.DoesNotContain("任务未完成", events.Select(item => item.Content));
    }

    [Fact]
    public async Task RunAsync_CompletesReadOnlyGoalWithNegatedMutationInstruction()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var goal = "请阅读当前项目的目录结构，简要说明 src 和 tests 目录分别负责什么。不需要修改文件。";
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([new ChatDelta { Content = "src 包含应用代码，tests 包含测试代码。" }]),
            new AgentToolCatalog([])));

        var events = new List<AgentHarnessEvent>();
        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = goal,
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = goal }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext { ProjectPath = Environment.CurrentDirectory }
                       }))
        {
            events.Add(item);
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.False(run.RequiresProjectMutation);
        Assert.DoesNotContain("任务未完成", events.Select(item => item.Content));
        Assert.Contains("结果一致性：未检测到需校验", run.FinalValidationSummary);
    }

    [Fact]
    public async Task RunAsync_FlagsRiskWhenFinalAnswerClaimsMutationWithoutMutationTool()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([new ChatDelta { Content = "已修改 README.md。" }]),
            new AgentToolCatalog([])));
        var events = new List<AgentHarnessEvent>();

        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "update readme",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "update readme" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext { ProjectPath = Environment.CurrentDirectory }
                       }))
        {
            events.Add(item);
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal("risk", run.CompletionEvidenceStatus);
        Assert.False(run.CanClaimModified);
        Assert.Contains("结果一致性：存在风险", run.FinalValidationSummary);
        Assert.Contains("没有成功的写入或提交工具记录", run.FinalValidationSummary);
        Assert.Contains(events, item => item.Content.Contains("完成声明已降级", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_FlagsRiskWhenFinalAnswerClaimsVerificationWithoutVerificationTool()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([new ChatDelta { Content = "已运行测试，全部通过。" }]),
            new AgentToolCatalog([])));
        var events = new List<AgentHarnessEvent>();

        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "check tests",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "check tests" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext { ProjectPath = Environment.CurrentDirectory }
                       }))
        {
            events.Add(item);
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(AgentRunStatus.Completed, run.Status);
        Assert.Equal("risk", run.CompletionEvidenceStatus);
        Assert.False(run.CanClaimVerified);
        Assert.Contains("结果一致性：存在风险", run.FinalValidationSummary);
        Assert.Contains("没有成功的验证工具记录", run.FinalValidationSummary);
        Assert.Contains(events, item => item.Content.Contains("完成声明已降级", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompletionEvidence_FlagsRiskWhenFinalAnswerClaimsVerificationButOneVerificationFailed()
    {
        var run = new AgentRun
        {
            Verifications =
            [
                new AgentVerification { Command = "dotnet build", IsSuccess = true },
                new AgentVerification { Command = "dotnet test", IsSuccess = false, ExitCode = 1 }
            ]
        };
        var report = new AgentCompletionEvidenceChecker().Check("已运行测试，全部通过。", run);

        Assert.Equal("risk", report.Status);
        Assert.False(report.CanClaimVerified);
        Assert.Contains("仍有失败的验证记录", report.Risks.Single());
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
                [new ChatDelta { Content = "已运行测试，全部通过。" }]
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
        Assert.Contains("结果一致性：声明与工具记录一致", run.FinalValidationSummary);
        Assert.Contains("复查并继续", run.RecoverySuggestion);
    }

    [Fact]
    public async Task RunAsync_FailsRunWhenVerificationToolFails()
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
            new AgentToolCatalog([new FakeFailingVerificationTool()])));

        var events = new List<AgentHarnessEvent>();
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
            events.Add(item);
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(AgentRunStatus.Failed, run.Status);
        Assert.Equal("At least one verification failed.", run.FinalStatusReason);
        Assert.Contains("验证：0/1 通过", run.FinalValidationSummary);
        Assert.Contains("上一轮验证未全部通过", run.RecoverySuggestion);
        Assert.Contains("恢复包", run.RecoverySuggestion);
        Assert.Contains("失败验证恢复包", run.RecoverySuggestion);
        Assert.Contains("失败验证：1/1", run.VerificationRecoveryPacket);
        Assert.Contains("dotnet test", run.VerificationRecoveryPacket);
        Assert.Contains("Failed", run.VerificationRecoveryPacket);
        Assert.Contains("恢复动作：先复现失败命令", run.VerificationRecoveryPacket);
        Assert.Contains("最近错误", run.CheckpointSummary);
        Assert.Contains("只修复导致验证失败的最小问题", run.RecoverySuggestion);
        Assert.Contains(events, item => item.Type == AgentHarnessEventType.ContentDelta &&
                                       item.Content.Contains("验证未通过", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_BuildsStructuredRecoveryPromptForRejectedToolApproval()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var toolCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "apply_patch",
            ArgumentsJson = """
            {
              "changes": [
                { "path": "file.txt", "old_text": "old", "new_text": "new" }
              ]
            }
            """
        };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([
                [new ChatDelta { ToolCalls = [toolCall] }],
                [new ChatDelta { Content = "blocked" }]
            ]),
            new AgentToolCatalog([new ApplyPatchTool()])));

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "update file",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "update file" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Reject("not now"))
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.Equal(1, run.ToolApprovalRejectedCount);
        Assert.Contains("工具审批：需要 1 次，拒绝 1 次", run.CheckpointSummary);
        Assert.Contains("被工具审批中断", run.RecoverySuggestion);
        Assert.Contains("如果用户没有重新授权", run.RecoverySuggestion);
        Assert.Contains("恢复包", run.RecoverySuggestion);
    }

    [Fact]
    public async Task RunAsync_RecordsPlanFromUpdatePlanTool()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var toolCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "update_plan",
            ArgumentsJson = """
            {
              "summary": "Fix the login bug",
              "items": [
                { "title": "Read auth code", "status": "completed", "notes": "Done" },
                { "title": "Fix the bug", "status": "in_progress" },
                { "title": "Run tests", "status": "pending" }
              ]
            }
            """
        };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([
                [new ChatDelta { ToolCalls = [toolCall] }],
                [new ChatDelta { Content = "done" }]
            ]),
            new AgentToolCatalog([new UpdatePlanTool()])));

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "fix login",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "fix login" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve())
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.NotNull(run.Plan);
        Assert.Equal("Fix the login bug", run.Plan!.Summary);
        Assert.Equal(3, run.Plan.Items.Count);
        Assert.Equal("Read auth code", run.Plan.Items[0].Title);
        Assert.Equal(AgentPlanItemStatus.Completed, run.Plan.Items[0].Status);
        Assert.Equal("Done", run.Plan.Items[0].Notes);
        Assert.Equal("Fix the bug", run.Plan.Items[1].Title);
        Assert.Equal(AgentPlanItemStatus.InProgress, run.Plan.Items[1].Status);
        Assert.Equal("Run tests", run.Plan.Items[2].Title);
        Assert.Equal(AgentPlanItemStatus.Pending, run.Plan.Items[2].Status);
        Assert.Contains("调用工具：update_plan", run.Steps.Select(step => step.Title));
    }

    [Fact]
    public async Task RunAsync_UpdatesExistingPlanOnSubsequentCalls()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var firstCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "update_plan",
            ArgumentsJson = """
            {
              "summary": "Fix the bug",
              "items": [
                { "title": "Read code", "status": "pending" },
                { "title": "Fix it", "status": "pending" }
              ]
            }
            """
        };
        var secondCall = new ChatToolCall
        {
            Id = "tool-call-2",
            Name = "update_plan",
            ArgumentsJson = """
            {
              "summary": "Fix the bug",
              "items": [
                { "title": "Read code", "status": "completed", "notes": "Done reading" },
                { "title": "Fix it", "status": "in_progress" }
              ]
            }
            """
        };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([
                [new ChatDelta { ToolCalls = [firstCall] }],
                [new ChatDelta { ToolCalls = [secondCall] }],
                [new ChatDelta { Content = "done" }]
            ]),
            new AgentToolCatalog([new UpdatePlanTool()])));

        await foreach (var _ in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "fix bug",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "fix bug" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve())
                           }
                       }))
        {
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.NotNull(run.Plan);
        Assert.Equal("Fix the bug", run.Plan!.Summary);
        Assert.Equal(2, run.Plan.Items.Count);
        Assert.Equal(AgentPlanItemStatus.Completed, run.Plan.Items[0].Status);
        Assert.Equal("Done reading", run.Plan.Items[0].Notes);
        Assert.Equal(AgentPlanItemStatus.InProgress, run.Plan.Items[1].Status);
    }

    [Fact]
    public async Task RunAsync_AutoVerifiesAfterMutationWhenConfigured()
    {
        using var workspace = TemporaryWorkspace.Create();
        var targetPath = Path.Combine(workspace.Path, "file.txt");
        await File.WriteAllTextAsync(targetPath, "old content");

        var conversation = new Conversation { Id = "conversation-1" };
        var toolCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "apply_patch",
            ArgumentsJson = """
            {
              "changes": [
                { "path": "file.txt", "old_text": "old content", "new_text": "new content" }
              ]
            }
            """
        };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([
                [new ChatDelta { ToolCalls = [toolCall] }],
                [new ChatDelta { Content = "done" }]
            ]),
            new AgentToolCatalog([new ApplyPatchTool(), new FakeVerificationTool()])));

        var events = new List<AgentHarnessEvent>();
        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "update file",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "update file" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = workspace.Path,
                               RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve()),
                               AutoVerifyAgentRuns = true,
                               MaxAutoFixRounds = 1,
                               VerificationCommands =
                               [
                                   new ProjectVerificationCommand
                                   {
                                       Name = "build",
                                       Command = "dotnet build",
                                       IsDefault = true
                                   }
                               ]
                           }
                       }))
        {
            events.Add(item);
        }

        var run = Assert.Single(conversation.AgentRuns);
        Assert.True(run.MutationToolSucceeded);
        // Auto-verify should have been triggered, producing at least one verification
        Assert.True(run.Verifications.Count > 0, "Expected auto-verify to produce verification records");
        // Should have tool call events for the verification
        Assert.Contains(events, e => e.Type == AgentHarnessEventType.ToolCall);
    }

    [Fact]
    public async Task RunAsync_AutoVerifiesAllowlistedShellCommandWithArguments()
    {
        using var workspace = TemporaryWorkspace.Create();
        var targetPath = Path.Combine(workspace.Path, "file.txt");
        await File.WriteAllTextAsync(targetPath, "old content");

        var conversation = new Conversation { Id = "conversation-1" };
        var toolCall = new ChatToolCall
        {
            Id = "tool-call-1",
            Name = "apply_patch",
            ArgumentsJson = """
            {
              "changes": [
                { "path": "file.txt", "old_text": "old content", "new_text": "new content" }
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

        var events = new List<AgentHarnessEvent>();
        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "update file",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "update file" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = workspace.Path,
                               RequestToolApprovalAsync = (_, _) => Task.FromResult(ToolApprovalDecision.Approve()),
                               AutoVerifyAgentRuns = true,
                               MaxAutoFixRounds = 1,
                               VerificationCommands =
                               [
                                   new ProjectVerificationCommand
                                   {
                                       Name = "custom check",
                                       Command = "echo auto verify",
                                       TimeoutSeconds = 10
                                   }
                               ]
                           }
                       }))
        {
            events.Add(item);
        }

        var run = Assert.Single(conversation.AgentRuns);
        var verification = Assert.Single(run.Verifications);
        Assert.Equal("echo auto verify", verification.Command);
        Assert.True(verification.IsSuccess);
        Assert.Contains("auto verify", verification.Output);
        Assert.Contains(events, e => e.ToolCall?.Name == "run_shell" && e.ToolCall.ArgumentsJson.Contains("echo auto verify"));
    }

    [Fact]
    public async Task RunAsync_BudgetExceededPausesRunWithCheckpointAndContinuationPrompt()
    {
        var conversation = new Conversation { Id = "conversation-1" };
        var first = new ChatToolCall { Id = "tool-call-1", Name = "read_file", ArgumentsJson = "{}" };
        var second = new ChatToolCall { Id = "tool-call-2", Name = "read_file", ArgumentsJson = "{}" };
        var harness = new AgentHarness(new AgentRunner(
            new FakeChatCompletionService([
                [new ChatDelta { ToolCalls = [first, second] }]
            ]),
            new AgentToolCatalog([new FakeReadTool()])));

        var events = new List<AgentHarnessEvent>();
        await foreach (var item in harness.RunAsync(new AgentHarnessRunRequest
                       {
                           Conversation = conversation,
                           UserMessageId = "user-1",
                           AssistantMessageId = "assistant-1",
                           Goal = "inspect files",
                           ChatRequest = new ChatRequest
                           {
                               Model = "test",
                               Messages = [new ChatMessage { Role = ChatRole.User, Content = "inspect files" }]
                           },
                           Settings = new AppSettings { Model = "test" },
                           Context = new AgentRunContext
                           {
                               ProjectPath = Environment.CurrentDirectory,
                               EnabledToolIds = ["read_file"],
                               MaxToolRounds = 1,
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
        Assert.Equal(AgentRunStatus.BudgetExceeded, run.Status);
        Assert.Equal("Tool budget exhausted; checkpoint created.", run.FinalStatusReason);
        Assert.Equal("waiting_for_user", run.Phase);
        Assert.True(run.ToolBudgetExceeded);
        Assert.Contains("工具调用：1/1", run.CheckpointSummary);
        Assert.Contains("工具审批：需要 0 次，拒绝 0 次", run.CheckpointSummary);
        Assert.Contains("最终状态：Tool budget exhausted; checkpoint created.", run.CheckpointSummary);
        Assert.Contains("恢复包", run.RecoverySuggestion);
        Assert.Contains("先用 git_status", run.RecoverySuggestion);
        Assert.Contains("不要重复恢复包里已经完成", run.RecoverySuggestion);
        Assert.Contains(events, item => item.Type == AgentHarnessEventType.ContentDelta &&
                                       item.Content.Contains("任务已暂停", StringComparison.Ordinal));
        Assert.Contains(events, item => item.Type == AgentHarnessEventType.RunCompleted &&
                                       item.Run?.Status == AgentRunStatus.BudgetExceeded);
        Assert.Contains(run.Steps, step => step.Title == "预算暂停");
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
            Requests.Add(request);
            var deltas = _responses.Count > 0 ? _responses.Dequeue() : [];
            foreach (var delta in deltas)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return delta;
                await Task.Yield();
            }
        }

        public List<ChatRequest> Requests { get; } = [];
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

    private sealed class FakeFailingVerificationTool : IAgentTool
    {
        public string Id => "run_test";
        public AgentToolRisk Risk => AgentToolRisk.Shell;
        public ChatToolDefinition Definition { get; } = new()
        {
            Name = "run_test",
            Description = "fake failing test",
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
                Summary = "run fake failing tests",
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
                IsError = true,
                Content = """
                {
                  "command": "dotnet test",
                  "exitCode": 1,
                  "timedOut": false,
                  "output": "Failed"
                }
                """
            });
        }
    }

    private sealed class FakeReadTool : IAgentTool
    {
        public string Id => "read_file";
        public AgentToolRisk Risk => AgentToolRisk.ReadOnly;
        public ChatToolDefinition Definition { get; } = new()
        {
            Name = "read_file",
            Description = "fake read",
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
                Summary = "read"
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
                Content = "file content"
            });
        }
    }
}
