using System.Text.Json;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Domain;

public sealed class AgentRunSerializationTests
{
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
                            Output = "Passed"
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
        Assert.Equal(AgentStepType.ToolCall, roundTripped.AgentRuns[0].Steps[0].Type);
        Assert.Equal("src/App.cs", roundTripped.AgentRuns[0].FileChanges[0].Path);
        Assert.Equal(18, roundTripped.AgentRuns[0].FileChanges[0].NewChars);
        Assert.True(roundTripped.AgentRuns[0].Verifications[0].IsSuccess);
    }
}
