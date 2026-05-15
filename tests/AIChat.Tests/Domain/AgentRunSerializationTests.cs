using System.Text.Json;
using AIChat.Domain.Chat;
using AIChat.Domain.Memory;

namespace AIChat.Tests.Domain;

public sealed class AgentRunSerializationTests
{
    [Theory]
    [InlineData(AgentRunStatus.Completed, "completed")]
    [InlineData(AgentRunStatus.BudgetExceeded, "waiting_for_user")]
    [InlineData(AgentRunStatus.Cancelled, "cancelled")]
    [InlineData(AgentRunStatus.Failed, "failed")]
    public void Complete_UpdatesStatusPhaseAndCompletionTime(AgentRunStatus status, string expectedPhase)
    {
        var completedAt = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
        var run = new AgentRun
        {
            Phase = "verifying"
        };

        run.Complete(status, completedAt, "done because test");

        Assert.Equal(status, run.Status);
        Assert.Equal(expectedPhase, run.Phase);
        Assert.Equal("done because test", run.CompletionReason);
        Assert.Equal(completedAt, run.CompletedAt);
    }

    [Fact]
    public void Conversation_RoundTripsAgentRunAndMessageLink()
    {
        var conversation = new Conversation
        {
            Id = "conversation-1",
            Messages =
            [
                new ChatMessage
                {
                    Id = "assistant-1",
                    Role = ChatRole.Assistant,
                    AgentRunId = "run-1",
                    Content = "done"
                },
                new ChatMessage
                {
                    Id = "user-with-image",
                    Role = ChatRole.User,
                    Content = "check this screenshot",
                    ContentParts =
                    [
                        ChatContentPart.ImagePart("image/png", "AQIDBA==", "screen.png")
                    ]
                }
            ],
            AgentRuns =
            [
                new AgentRun
                {
                    Id = "run-1",
                    ConversationId = "conversation-1",
                    AssistantMessageId = "assistant-1",
                    Phase = "verifying",
                    CompletionReason = "all tests passed",
                    ProjectPath = "D:/Code/AIChat",
                    Model = "test-model",
                    EnabledTools = ["read_file", "run_test"],
                    ToolPermissionModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["read_file"] = "AutoReadOnly",
                        ["run_test"] = "ConfirmEachTime"
                    },
                    WorkspaceBranch = "## main",
                    WorkspaceChangeCountAtStart = 3,
                    WorkspaceChangesWereTruncated = true,
                    ProjectPreparationSucceeded = true,
                    ProjectPreparationSummary = "路径可用 · AGENTS.md 已就绪 · 2 个验证命令",
                    ProjectAgentsAvailableAtStart = true,
                    ProjectVerificationCommandCountAtStart = 2,
                    MaxToolRounds = 4,
                    ToolCallCount = 2,
                    ModelCallCount = 3,
                    ContextEstimatedTokens = 420,
                    ContextRefCount = 6,
                    ToolBudgetExceeded = true,
                    RequiresProjectMutation = true,
                    MutationToolSucceeded = true,
                    ToolApprovalRequiredCount = 2,
                    ToolApprovalRejectedCount = 1,
                    ToolSessionAllowedCount = 1,
                    FinalValidationSummary = "工具预算：未耗尽",
                    CompletionEvidenceStatus = "satisfied",
                    CompletionEvidenceSummary = "结果一致性：声明与工具记录一致",
                    CanClaimModified = true,
                    CanClaimVerified = true,
                    ExecutionPolicySummary = "complexity=Complex; maxToolRounds=4",
                    FinalStatusReason = "Completion evidence satisfied.",
                    QualityScore = 91,
                    QualitySummary = "任务完成；验证通过 1 个",
                    StrategySuggestion = "策略表现良好，保持当前执行模式。",
                    TaskComplexity = "Complex",
                    PlannerUsed = true,
                    ExplorerUsed = true,
                    ExplorerDecisionReason = "Explorer scheduled: 1.",
                    RecoverySuggestion = "继续处理：test",
                    CheckpointSummary = "目标：test",
                    CheckpointArtifactRefs = ["read_file:tool_result:artifact-1"],
                    AcceptanceStatus = AgentRunAcceptanceStatus.Accepted,
                    AcceptanceNote = "用户确认通过",
                    AcceptanceReviewedAt = new DateTimeOffset(2026, 5, 1, 9, 2, 0, TimeSpan.Zero),
                    CurrentPhaseSummary = "running tests",
                    Status = AgentRunStatus.Completed,
                    StructuredPlan = new AgentStructuredPlan
                    {
                        RunId = "run-1",
                        Summary = "structured summary",
                        Budget = new AgentPlanBudget { MaxToolCalls = 4, TokenBudget = 8000 },
                        SubAgents =
                        [
                            new AgentPlannedSubAgent
                            {
                                TemplateId = "explorer",
                                Phase = "gathering_context",
                                Task = "Inspect service",
                                Reason = "Need focused context",
                                MaxToolCalls = 3,
                                DependsOn = ["plan"],
                                Order = 0
                            }
                        ],
                        Phases =
                        [
                            new AgentPlanPhase
                            {
                                Name = "executing",
                                Objective = "do work",
                                Tasks =
                                [
                                    new AgentPlanTask
                                    {
                                        Phase = "executing",
                                        Title = "Patch code",
                                        Risk = AgentPlanRisk.High,
                                        SuggestedTools = ["apply_patch"],
                                        Budget = new AgentPlanBudget { MaxToolCalls = 2, TokenBudget = 3000 }
                                    }
                                ]
                            }
                        ]
                    },
                    PhaseHistory =
                    [
                        new AgentRunPhaseRecord
                        {
                            RunId = "run-1",
                            Phase = "verifying",
                            Status = "completed",
                            Summary = "running tests",
                            StartedAt = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                            CompletedAt = new DateTimeOffset(2026, 5, 1, 9, 1, 0, TimeSpan.Zero)
                        }
                    ],
                    Steps =
                    [
                        new AgentStep
                        {
                            RunId = "run-1",
                            Number = 1,
                            Type = AgentStepType.ToolCall,
                            Status = AgentStepStatus.Completed,
                            Title = "调用工具：read_file",
                            ToolName = "read_file"
                        }
                    ],
                    FileChanges =
                    [
                        new AgentFileChange
                        {
                            RunId = "run-1",
                            StepId = "step-1",
                            ToolCallId = "tool-call-1",
                            ToolName = "apply_patch",
                            Path = "src/App.cs",
                            DiffText = "--- a/src/App.cs\n+++ b/src/App.cs",
                            OldChars = 12,
                            NewChars = 18
                        }
                    ],
                    Verifications =
                    [
                        new AgentVerification
                        {
                            RunId = "run-1",
                            StepId = "step-2",
                            ToolCallId = "tool-call-2",
                            ToolName = "run_test",
                            Command = "dotnet test AIChat.sln",
                            ExitCode = 0,
                            IsSuccess = true,
                            Output = "Passed",
                            Summary = "All tests passed"
                        }
                    ],
                    Artifacts =
                    [
                        new AgentArtifact
                        {
                            RunId = "run-1",
                            StepId = "step-2",
                            ToolCallId = "tool-call-2",
                            ToolName = "run_test",
                            Kind = "tool_result",
                            Summary = "large test output",
                            Content = "full output"
                        }
                    ],
                    SubAgentScheduleDecisions =
                    [
                        new AgentSubAgentScheduleDecision
                        {
                            RunId = "run-1",
                            PlannedSubAgentId = "planned-sub-1",
                            TemplateId = "explorer",
                            Phase = "gathering_context",
                            Task = "Inspect service",
                            Reason = "Need focused context",
                            Status = "Skipped",
                            SkipReason = "Duplicate sub-agent task.",
                            MaxToolCalls = 3,
                            DependsOn = ["plan"],
                            Order = 0
                        }
                    ],
                    SubAgentRuns =
                    [
                        new AgentSubAgentRun
                        {
                            Id = "sub-1",
                            ParentRunId = "run-1",
                            TemplateId = "explorer",
                            Task = "Inspect service",
                            Status = "Completed",
                            Summary = "Found service",
                            RecommendedNextStep = "Use findings",
                            MaxToolCalls = 4,
                            ToolCallCount = 1,
                            Findings = ["src/App.cs is relevant"],
                            ArtifactRefs = ["artifact: ref"],
                            ToolCalls =
                            [
                                new AgentSubAgentToolCall
                                {
                                    ToolCallId = "sub-tool-1",
                                    ToolName = "read_file",
                                    ArgumentsJson = "{}",
                                    ResultSummary = "ok"
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(conversation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<Conversation>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal("run-1", roundTripped.Messages[0].AgentRunId);
        Assert.Single(roundTripped.Messages[1].ContentParts);
        Assert.Equal("image/png", roundTripped.Messages[1].ContentParts[0].MediaType);
        Assert.Single(roundTripped.AgentRuns);
        Assert.Single(roundTripped.AgentRuns[0].Steps);
        Assert.Single(roundTripped.AgentRuns[0].FileChanges);
        Assert.Single(roundTripped.AgentRuns[0].Verifications);
        Assert.Single(roundTripped.AgentRuns[0].Artifacts);
        Assert.Single(roundTripped.AgentRuns[0].SubAgentScheduleDecisions);
        Assert.Single(roundTripped.AgentRuns[0].SubAgentRuns);
        Assert.Equal("verifying", roundTripped.AgentRuns[0].Phase);
        Assert.Equal("all tests passed", roundTripped.AgentRuns[0].CompletionReason);
        Assert.Equal("D:/Code/AIChat", roundTripped.AgentRuns[0].ProjectPath);
        Assert.Equal("test-model", roundTripped.AgentRuns[0].Model);
        Assert.Equal(["read_file", "run_test"], roundTripped.AgentRuns[0].EnabledTools);
        Assert.Equal("ConfirmEachTime", roundTripped.AgentRuns[0].ToolPermissionModes["run_test"]);
        Assert.Equal("## main", roundTripped.AgentRuns[0].WorkspaceBranch);
        Assert.Equal(3, roundTripped.AgentRuns[0].WorkspaceChangeCountAtStart);
        Assert.True(roundTripped.AgentRuns[0].WorkspaceChangesWereTruncated);
        Assert.True(roundTripped.AgentRuns[0].ProjectPreparationSucceeded);
        Assert.Equal("路径可用 · AGENTS.md 已就绪 · 2 个验证命令", roundTripped.AgentRuns[0].ProjectPreparationSummary);
        Assert.True(roundTripped.AgentRuns[0].ProjectAgentsAvailableAtStart);
        Assert.Equal(2, roundTripped.AgentRuns[0].ProjectVerificationCommandCountAtStart);
        Assert.Equal(4, roundTripped.AgentRuns[0].MaxToolRounds);
        Assert.Equal(2, roundTripped.AgentRuns[0].ToolCallCount);
        Assert.Equal(3, roundTripped.AgentRuns[0].ModelCallCount);
        Assert.Equal(420, roundTripped.AgentRuns[0].ContextEstimatedTokens);
        Assert.Equal(6, roundTripped.AgentRuns[0].ContextRefCount);
        Assert.True(roundTripped.AgentRuns[0].ToolBudgetExceeded);
        Assert.True(roundTripped.AgentRuns[0].RequiresProjectMutation);
        Assert.True(roundTripped.AgentRuns[0].MutationToolSucceeded);
        Assert.Equal(2, roundTripped.AgentRuns[0].ToolApprovalRequiredCount);
        Assert.Equal(1, roundTripped.AgentRuns[0].ToolApprovalRejectedCount);
        Assert.Equal(1, roundTripped.AgentRuns[0].ToolSessionAllowedCount);
        Assert.Equal("工具预算：未耗尽", roundTripped.AgentRuns[0].FinalValidationSummary);
        Assert.Equal("satisfied", roundTripped.AgentRuns[0].CompletionEvidenceStatus);
        Assert.Equal("结果一致性：声明与工具记录一致", roundTripped.AgentRuns[0].CompletionEvidenceSummary);
        Assert.True(roundTripped.AgentRuns[0].CanClaimModified);
        Assert.True(roundTripped.AgentRuns[0].CanClaimVerified);
        Assert.Equal("complexity=Complex; maxToolRounds=4", roundTripped.AgentRuns[0].ExecutionPolicySummary);
        Assert.Equal("Completion evidence satisfied.", roundTripped.AgentRuns[0].FinalStatusReason);
        Assert.Equal(91, roundTripped.AgentRuns[0].QualityScore);
        Assert.Equal("任务完成；验证通过 1 个", roundTripped.AgentRuns[0].QualitySummary);
        Assert.Equal("策略表现良好，保持当前执行模式。", roundTripped.AgentRuns[0].StrategySuggestion);
        Assert.Equal("Complex", roundTripped.AgentRuns[0].TaskComplexity);
        Assert.True(roundTripped.AgentRuns[0].PlannerUsed);
        Assert.True(roundTripped.AgentRuns[0].ExplorerUsed);
        Assert.Equal("Explorer scheduled: 1.", roundTripped.AgentRuns[0].ExplorerDecisionReason);
        Assert.Equal("继续处理：test", roundTripped.AgentRuns[0].RecoverySuggestion);
        Assert.Equal("目标：test", roundTripped.AgentRuns[0].CheckpointSummary);
        Assert.Equal(["read_file:tool_result:artifact-1"], roundTripped.AgentRuns[0].CheckpointArtifactRefs);
        Assert.Equal(AgentRunAcceptanceStatus.Accepted, roundTripped.AgentRuns[0].AcceptanceStatus);
        Assert.Equal("用户确认通过", roundTripped.AgentRuns[0].AcceptanceNote);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 9, 2, 0, TimeSpan.Zero), roundTripped.AgentRuns[0].AcceptanceReviewedAt);
        Assert.Equal("running tests", roundTripped.AgentRuns[0].CurrentPhaseSummary);
        Assert.Single(roundTripped.AgentRuns[0].PhaseHistory);
        Assert.Equal("verifying", roundTripped.AgentRuns[0].PhaseHistory[0].Phase);
        Assert.Equal("completed", roundTripped.AgentRuns[0].PhaseHistory[0].Status);
        Assert.NotNull(roundTripped.AgentRuns[0].StructuredPlan);
        Assert.Equal("structured summary", roundTripped.AgentRuns[0].StructuredPlan!.Summary);
        Assert.Single(roundTripped.AgentRuns[0].StructuredPlan!.SubAgents);
        Assert.Equal("Inspect service", roundTripped.AgentRuns[0].StructuredPlan!.SubAgents[0].Task);
        Assert.Equal(["plan"], roundTripped.AgentRuns[0].StructuredPlan!.SubAgents[0].DependsOn);
        Assert.Equal(AgentPlanRisk.High, roundTripped.AgentRuns[0].StructuredPlan!.Phases[0].Tasks[0].Risk);
        Assert.Equal(AgentStepType.ToolCall, roundTripped.AgentRuns[0].Steps[0].Type);
        Assert.Equal("src/App.cs", roundTripped.AgentRuns[0].FileChanges[0].Path);
        Assert.Equal(18, roundTripped.AgentRuns[0].FileChanges[0].NewChars);
        Assert.True(roundTripped.AgentRuns[0].Verifications[0].IsSuccess);
        Assert.Equal("All tests passed", roundTripped.AgentRuns[0].Verifications[0].Summary);
        Assert.Equal("full output", roundTripped.AgentRuns[0].Artifacts[0].Content);
        Assert.Equal("Skipped", roundTripped.AgentRuns[0].SubAgentScheduleDecisions[0].Status);
        Assert.Equal("Duplicate sub-agent task.", roundTripped.AgentRuns[0].SubAgentScheduleDecisions[0].SkipReason);
        Assert.Equal("explorer", roundTripped.AgentRuns[0].SubAgentRuns[0].TemplateId);
        Assert.Single(roundTripped.AgentRuns[0].SubAgentRuns[0].ToolCalls);
        Assert.Equal("read_file", roundTripped.AgentRuns[0].SubAgentRuns[0].ToolCalls[0].ToolName);
    }

    [Fact]
    public void Conversation_RoundTripsRunSourceIds()
    {
        var conversation = new Conversation
        {
            Id = "conversation-1",
            Messages = [],
            AgentRuns =
            [
                new AgentRun
                {
                    Id = "run-2",
                    Status = AgentRunStatus.Completed,
                    ContinuedFromRunId = "run-1",
                    RetriedFromRunId = "run-0"
                }
            ]
        };

        var json = JsonSerializer.Serialize(conversation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<Conversation>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal("run-1", roundTripped.AgentRuns[0].ContinuedFromRunId);
        Assert.Equal("run-0", roundTripped.AgentRuns[0].RetriedFromRunId);
    }

    [Fact]
    public void Conversation_RoundTripsAgentPlan()
    {
        var conversation = new Conversation
        {
            Id = "conversation-1",
            Messages =
            [
                new ChatMessage
                {
                    Id = "assistant-1",
                    Role = ChatRole.Assistant,
                    AgentRunId = "run-1",
                    Content = "done"
                }
            ],
            AgentRuns =
            [
                new AgentRun
                {
                    Id = "run-1",
                    ConversationId = "conversation-1",
                    AssistantMessageId = "assistant-1",
                    Status = AgentRunStatus.Completed,
                    Plan = new AgentPlan
                    {
                        Id = "plan-1",
                        RunId = "run-1",
                        Summary = "Fix the login bug",
                        CreatedAt = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero),
                        UpdatedAt = new DateTimeOffset(2026, 5, 1, 9, 5, 0, TimeSpan.Zero),
                        Items =
                        [
                            new AgentPlanItem
                            {
                                Id = "item-1",
                                Title = "Read the auth code",
                                Status = AgentPlanItemStatus.Completed,
                                Notes = "Done reading",
                                Order = 0
                            },
                            new AgentPlanItem
                            {
                                Id = "item-2",
                                Title = "Fix the bug",
                                Status = AgentPlanItemStatus.InProgress,
                                Notes = "",
                                Order = 1
                            },
                            new AgentPlanItem
                            {
                                Id = "item-3",
                                Title = "Run tests",
                                Status = AgentPlanItemStatus.Pending,
                                Order = 2
                            }
                        ]
                    }
                }
            ]
        };

        var json = JsonSerializer.Serialize(conversation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<Conversation>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        var run = roundTripped.AgentRuns[0];
        Assert.NotNull(run.Plan);
        Assert.Equal("plan-1", run.Plan!.Id);
        Assert.Equal("run-1", run.Plan.RunId);
        Assert.Equal("Fix the login bug", run.Plan.Summary);
        Assert.Equal(3, run.Plan.Items.Count);
        Assert.Equal("Read the auth code", run.Plan.Items[0].Title);
        Assert.Equal(AgentPlanItemStatus.Completed, run.Plan.Items[0].Status);
        Assert.Equal("Done reading", run.Plan.Items[0].Notes);
        Assert.Equal(0, run.Plan.Items[0].Order);
        Assert.Equal("Fix the bug", run.Plan.Items[1].Title);
        Assert.Equal(AgentPlanItemStatus.InProgress, run.Plan.Items[1].Status);
        Assert.Equal("Run tests", run.Plan.Items[2].Title);
        Assert.Equal(AgentPlanItemStatus.Pending, run.Plan.Items[2].Status);
    }

    [Fact]
    public void Conversation_RoundTripsFileChangeSnapshotAndHash()
    {
        var conversation = new Conversation
        {
            Id = "conversation-1",
            Messages = [],
            AgentRuns =
            [
                new AgentRun
                {
                    Id = "run-1",
                    Status = AgentRunStatus.Completed,
                    FileChanges =
                    [
                        new AgentFileChange
                        {
                            RunId = "run-1",
                            Path = "src/App.cs",
                            DiffText = "--- a/src/App.cs\n+++ b/src/App.cs",
                            OldChars = 100,
                            NewChars = 120,
                            ContentSnapshot = "original file content here",
                            PostChangeHash = "abc123def456"
                        }
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(conversation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<Conversation>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        var change = roundTripped.AgentRuns[0].FileChanges[0];
        Assert.Equal("original file content here", change.ContentSnapshot);
        Assert.Equal("abc123def456", change.PostChangeHash);
    }

    [Fact]
    public void Conversation_HandlesMissingPlanGracefully()
    {
        var conversation = new Conversation
        {
            Id = "conversation-1",
            Messages = [],
            AgentRuns =
            [
                new AgentRun
                {
                    Id = "run-1",
                    Status = AgentRunStatus.Completed
                }
            ]
        };

        var json = JsonSerializer.Serialize(conversation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<Conversation>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Single(roundTripped.AgentRuns);
        Assert.Null(roundTripped.AgentRuns[0].Plan);
    }

    [Fact]
    public void ProjectWorkspace_RoundTripsProjectToolPermissionModes()
    {
        var workspace = new AIChat.Domain.Projects.ProjectWorkspace
        {
            Id = "project-1",
            Name = "TestProject",
            Path = "D:/Code/TestProject",
            ProjectToolPermissionModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["write_file"] = "AutoReadOnly",
                ["run_shell"] = "Disabled"
            },
            VerificationCommands =
            [
                new AIChat.Domain.Projects.ProjectVerificationCommand
                {
                    Name = "测试",
                    Command = "dotnet test",
                    WorkingDirectory = "AIChat.sln",
                    TimeoutSeconds = 180,
                    IsDefault = true
                }
            ],
            Memories =
            [
                new MemoryEntry
                {
                    ProjectId = "project-1",
                    Category = MemoryCategory.Project,
                    Content = "Use MVVM.",
                    Source = "test"
                }
            ],
            PendingMemories =
            [
                new MemoryEntry
                {
                    ProjectId = "project-1",
                    Category = MemoryCategory.User,
                    Content = "Prefers concise replies.",
                    Source = "candidate"
                }
            ]
        };

        var json = JsonSerializer.Serialize(workspace, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<AIChat.Domain.Projects.ProjectWorkspace>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal(2, roundTripped.ProjectToolPermissionModes.Count);
        Assert.Equal("AutoReadOnly", roundTripped.ProjectToolPermissionModes["write_file"]);
        Assert.Equal("Disabled", roundTripped.ProjectToolPermissionModes["run_shell"]);
        Assert.Single(roundTripped.VerificationCommands);
        Assert.Equal("dotnet test", roundTripped.VerificationCommands[0].Command);
        Assert.Equal("AIChat.sln", roundTripped.VerificationCommands[0].WorkingDirectory);
        Assert.True(roundTripped.VerificationCommands[0].IsDefault);
        Assert.Single(roundTripped.Memories);
        Assert.Equal("Use MVVM.", roundTripped.Memories[0].Content);
        Assert.Single(roundTripped.PendingMemories);
        Assert.Equal("Prefers concise replies.", roundTripped.PendingMemories[0].Content);
    }

    [Fact]
    public void ProjectWorkspace_HandlesEmptyProjectToolPermissionModes()
    {
        var workspace = new AIChat.Domain.Projects.ProjectWorkspace
        {
            Id = "project-1",
            Name = "TestProject"
        };

        var json = JsonSerializer.Serialize(workspace, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<AIChat.Domain.Projects.ProjectWorkspace>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Empty(roundTripped.ProjectToolPermissionModes);
    }

    [Fact]
    public void ConfiguredProvider_RoundTripsVisionOverride()
    {
        var provider = new AIChat.Abstractions.Llm.ConfiguredLlmProvider
        {
            TemplateId = "deepseek",
            ApiKey = "key",
            SupportsVisionOverride = true
        };

        var json = JsonSerializer.Serialize(provider, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<AIChat.Abstractions.Llm.ConfiguredLlmProvider>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.True(roundTripped.SupportsVisionOverride);
    }
}
