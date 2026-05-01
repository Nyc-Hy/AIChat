using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Domain;

public sealed class AgentRunSerializationTests
{
    [Theory]
    [InlineData(AgentRunStatus.Completed, "completed")]
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
                    MaxToolRounds = 4,
                    ToolCallCount = 2,
                    ToolBudgetExceeded = true,
                    RequiresProjectMutation = true,
                    MutationToolSucceeded = true,
                    ToolApprovalRequiredCount = 2,
                    ToolApprovalRejectedCount = 1,
                    ToolSessionAllowedCount = 1,
                    FinalValidationSummary = "工具预算：未耗尽",
                    RecoverySuggestion = "继续处理：test",
                    Status = AgentRunStatus.Completed,
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
                    ]
                }
            ]
        };

        var json = JsonSerializer.Serialize(conversation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<Conversation>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal("run-1", roundTripped.Messages[0].AgentRunId);
        Assert.Single(roundTripped.AgentRuns);
        Assert.Single(roundTripped.AgentRuns[0].Steps);
        Assert.Single(roundTripped.AgentRuns[0].FileChanges);
        Assert.Single(roundTripped.AgentRuns[0].Verifications);
        Assert.Equal("verifying", roundTripped.AgentRuns[0].Phase);
        Assert.Equal("all tests passed", roundTripped.AgentRuns[0].CompletionReason);
        Assert.Equal("D:/Code/AIChat", roundTripped.AgentRuns[0].ProjectPath);
        Assert.Equal("test-model", roundTripped.AgentRuns[0].Model);
        Assert.Equal(["read_file", "run_test"], roundTripped.AgentRuns[0].EnabledTools);
        Assert.Equal("ConfirmEachTime", roundTripped.AgentRuns[0].ToolPermissionModes["run_test"]);
        Assert.Equal("## main", roundTripped.AgentRuns[0].WorkspaceBranch);
        Assert.Equal(3, roundTripped.AgentRuns[0].WorkspaceChangeCountAtStart);
        Assert.True(roundTripped.AgentRuns[0].WorkspaceChangesWereTruncated);
        Assert.Equal(4, roundTripped.AgentRuns[0].MaxToolRounds);
        Assert.Equal(2, roundTripped.AgentRuns[0].ToolCallCount);
        Assert.True(roundTripped.AgentRuns[0].ToolBudgetExceeded);
        Assert.True(roundTripped.AgentRuns[0].RequiresProjectMutation);
        Assert.True(roundTripped.AgentRuns[0].MutationToolSucceeded);
        Assert.Equal(2, roundTripped.AgentRuns[0].ToolApprovalRequiredCount);
        Assert.Equal(1, roundTripped.AgentRuns[0].ToolApprovalRejectedCount);
        Assert.Equal(1, roundTripped.AgentRuns[0].ToolSessionAllowedCount);
        Assert.Equal("工具预算：未耗尽", roundTripped.AgentRuns[0].FinalValidationSummary);
        Assert.Equal("继续处理：test", roundTripped.AgentRuns[0].RecoverySuggestion);
        Assert.Equal(AgentStepType.ToolCall, roundTripped.AgentRuns[0].Steps[0].Type);
        Assert.Equal("src/App.cs", roundTripped.AgentRuns[0].FileChanges[0].Path);
        Assert.Equal(18, roundTripped.AgentRuns[0].FileChanges[0].NewChars);
        Assert.True(roundTripped.AgentRuns[0].Verifications[0].IsSuccess);
        Assert.Equal("All tests passed", roundTripped.AgentRuns[0].Verifications[0].Summary);
    }

    [Fact]
    public void Conversation_RoundTripsContinuedFromRunId()
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
                    ContinuedFromRunId = "run-1"
                }
            ]
        };

        var json = JsonSerializer.Serialize(conversation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<Conversation>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal("run-1", roundTripped.AgentRuns[0].ContinuedFromRunId);
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
            }
        };

        var json = JsonSerializer.Serialize(workspace, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<AIChat.Domain.Projects.ProjectWorkspace>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(roundTripped);
        Assert.Equal(2, roundTripped.ProjectToolPermissionModes.Count);
        Assert.Equal("AutoReadOnly", roundTripped.ProjectToolPermissionModes["write_file"]);
        Assert.Equal("Disabled", roundTripped.ProjectToolPermissionModes["run_shell"]);
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
}
