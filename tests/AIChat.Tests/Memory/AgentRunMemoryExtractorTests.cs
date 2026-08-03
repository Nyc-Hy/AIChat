using AIChat.Application.Memory;
using AIChat.Domain.Chat;
using AIChat.Domain.Memory;

namespace AIChat.Tests.Memory;

public sealed class AgentRunMemoryExtractorTests
{
    [Fact]
    public void Extract_CreatesProjectTaskAndToolMemoriesForCompletedRun()
    {
        var conversation = new Project { Id = "c1", Title = "Test" };
        conversation.Messages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRole.User,
            Content = "修复登录验证"
        });
        var run = new AgentRun
        {
            Id = "r1",
            ConversationId = conversation.Id,
            Goal = "修复登录验证",
            Status = AgentRunStatus.Completed,
            FileChanges =
            [
                new AgentFileChange { Path = "src/Auth/LoginService.cs" },
                new AgentFileChange { Path = "tests/AuthTests.cs" }
            ],
            Verifications =
            [
                new AgentVerification
                {
                    Command = "dotnet test",
                    IsSuccess = true,
                    Summary = "All tests passed"
                }
            ]
        };

        var candidates = new AgentRunMemoryExtractor().Extract(conversation, run);

        Assert.Contains(candidates, candidate => candidate.Category == MemoryCategory.Project &&
                                                 candidate.Content.Contains("src/Auth/LoginService.cs"));
        Assert.Contains(candidates, candidate => candidate.Category == MemoryCategory.Task &&
                                                 candidate.Content.Contains("修复登录验证"));
        Assert.Contains(candidates, candidate => candidate.Category == MemoryCategory.Tool &&
                                                 candidate.Content.Contains("dotnet test"));
    }

    [Fact]
    public void Extract_DoesNotAutoStoreUserPreferenceCandidate()
    {
        var conversation = new Project { Id = "c1", Title = "Test" };
        conversation.Messages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRole.User,
            Content = "以后回答短一点"
        });
        var run = new AgentRun
        {
            Id = "r1",
            ConversationId = conversation.Id,
            Goal = "解释结构",
            Status = AgentRunStatus.Completed
        };

        var candidates = new AgentRunMemoryExtractor().Extract(conversation, run);

        var preference = Assert.Single(candidates, candidate => candidate.Category == MemoryCategory.User);
        Assert.True(preference.RequiresUserConfirmation);
    }

    [Fact]
    public void Extract_CreatesToolMemoryForFailedVerificationRun()
    {
        var conversation = new Project { Id = "c1", Title = "Test" };
        var run = new AgentRun
        {
            Id = "r1",
            ConversationId = conversation.Id,
            Goal = "修复测试",
            Status = AgentRunStatus.Failed,
            FileChanges = [new AgentFileChange { Path = "src/App.cs" }],
            Verifications =
            [
                new AgentVerification
                {
                    Command = "dotnet test",
                    ExitCode = 1,
                    IsSuccess = false,
                    Summary = "AppTests.cs(12): error CS1002"
                }
            ]
        };

        var candidates = new AgentRunMemoryExtractor().Extract(conversation, run);

        var memory = Assert.Single(candidates, candidate => candidate.Metadata["kind"] == "verification-failure");
        Assert.Equal(MemoryCategory.Tool, memory.Category);
        Assert.Contains("dotnet test", memory.Content);
        Assert.Contains("exit 1", memory.Content);
        Assert.Contains("AppTests.cs", memory.Content);
        Assert.Contains("src/App.cs", memory.Content);
    }

    [Fact]
    public void Extract_SkipsIncompleteRunsAndSecrets()
    {
        var conversation = new Project { Id = "c1", Title = "Test" };
        var run = new AgentRun
        {
            Id = "r1",
            ConversationId = conversation.Id,
            Goal = "save api_key for later",
            Status = AgentRunStatus.Completed,
            FileChanges = [new AgentFileChange { Path = "settings.json" }]
        };

        var candidates = new AgentRunMemoryExtractor().Extract(conversation, run);

        Assert.Empty(candidates);

        run.Status = AgentRunStatus.Failed;
        run.Goal = "normal task";
        Assert.Empty(new AgentRunMemoryExtractor().Extract(conversation, run));
    }
}
