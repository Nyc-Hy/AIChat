using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents;
using AIChat.Application.Context;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Domain.Artifacts;
using AIChat.Domain.Chat;
using AIChat.Domain.Memory;
using AIChat.Domain.Projects;

namespace AIChat.Tests.Agents;

public sealed class AgentRequestFactoryTests : IDisposable
{
    private readonly string _tempDir;

    public AgentRequestFactoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"aichat-request-factory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "Program.cs"), "Console.WriteLine(\"hi\");");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void CreateSnapshot_ExcludesAssistantPlaceholderAndProjectsRequestMetadata()
    {
        var conversation = CreateConversation();
        var assistant = AddMessage(conversation, ChatRole.Assistant, "正在连接模型...");

        var snapshot = AgentRequestFactory.CreateSnapshot(
            conversation,
            assistant.Id,
            CreateEffectiveSettings(),
            CreateRuntimeSettings(),
            ["read_file", "read_file", "write_file"]);

        Assert.Equal("DeepSeek", snapshot.Provider);
        Assert.Equal("deepseek-v4-pro", snapshot.Model);
        Assert.Equal(["read_file", "write_file"], snapshot.EnabledTools);
        Assert.DoesNotContain(snapshot.Messages, message => message.Id == assistant.Id);
        Assert.Contains(snapshot.Messages, message => message.Role == "user" && message.Content == "fix bug");
    }

    [Fact]
    public void CreateSnapshot_FromChatRequestIncludesContentPartMetadataWithoutBase64Payload()
    {
        var request = new ChatRequest
        {
            Model = "deepseek-v4-pro",
            Messages =
            [
                new ChatMessage
                {
                    Role = ChatRole.User,
                    Content = "inspect screenshot",
                    ContentParts =
                    [
                        ChatContentPart.ImagePart("image/png", "AQIDBA==", "screen.png")
                    ]
                }
            ]
        };

        var snapshot = AgentRequestFactory.CreateSnapshot(
            request,
            CreateEffectiveSettings(),
            CreateRuntimeSettings(),
            ["read_file"]);

        var message = Assert.Single(snapshot.Messages);
        var part = Assert.Single(message.ContentParts);
        Assert.Equal("image", part.Type);
        Assert.Equal("image/png", part.MediaType);
        Assert.Equal("screen.png", part.SourcePath);
        Assert.Equal(4, part.DataBytes);
        Assert.Equal("", part.Text);
    }

    [Fact]
    public void Build_CreatesChatRequestWithSystemContextAndConversationMessages()
    {
        var conversation = CreateConversation();
        var assistant = AddMessage(conversation, ChatRole.Assistant, "placeholder");
        var factory = CreateFactory();

        var result = factory.Build(new AgentRequestBuildRequest
        {
            Conversation = conversation,
            AssistantMessageId = assistant.Id,
            EffectiveSettings = CreateEffectiveSettings(),
            RuntimeSettings = CreateRuntimeSettings(),
            ProjectName = "Sample",
            ProjectPath = _tempDir,
            WorkspaceBranch = "main",
            WorkspaceChangeCount = 2,
            InputArtifacts =
            [
                new InputArtifact
                {
                    ConversationId = conversation.Id,
                    Kind = InputArtifactKind.Document,
                    FileName = "bug-report.pdf",
                    Summary = "Report says Program.cs prints the wrong message."
                }
            ],
            MemoryEntries =
            [
                new MemoryEntry
                {
                    ProjectId = "project-1",
                    Category = MemoryCategory.Project,
                    Content = "Program.cs is the sample entry point.",
                    Source = "test"
                }
            ]
        });

        Assert.Equal("deepseek-v4-pro", result.ChatRequest.Model);
        Assert.Equal(0.3, result.ChatRequest.Temperature);
        Assert.Equal(_tempDir, result.FileIndex.RootPath);
        Assert.Contains(result.FileIndex.Entries, entry => entry.RelativePath == "Program.cs");
        Assert.Contains("分支：main，未提交变更：2 个文件", result.WorkspaceSummary);
        Assert.NotNull(result.ContextPack);
        Assert.True(result.ContextPack.EstimatedTokens > 0);
        Assert.Equal(ChatRole.System, result.ChatRequest.Messages[0].Role);
        Assert.Contains("Context refs:", result.ChatRequest.Messages[0].Content);
        Assert.Contains("input-artifact:", result.ChatRequest.Messages[0].Content);
        Assert.Contains("bug-report.pdf", result.ChatRequest.Messages[0].Content);
        Assert.Contains("memory:", result.ChatRequest.Messages[0].Content);
        Assert.Contains(result.ChatRequest.Messages, message => message.Role == ChatRole.User && message.Content == "fix bug");
        Assert.DoesNotContain(result.ChatRequest.Messages, message => message.Id == assistant.Id);
    }

    [Fact]
    public void Build_CreatesAgentContextWithMergedProjectPermissionOverrides()
    {
        var conversation = CreateConversation();
        var assistant = AddMessage(conversation, ChatRole.Assistant, "placeholder");
        var factory = CreateFactory();

        var result = factory.Build(new AgentRequestBuildRequest
        {
            Conversation = conversation,
            AssistantMessageId = assistant.Id,
            EffectiveSettings = CreateEffectiveSettings(),
            RuntimeSettings = CreateRuntimeSettings(),
            ProjectPath = _tempDir,
            ProjectToolPermissionModes = new Dictionary<string, string>
            {
                ["write_file"] = nameof(ToolPermissionMode.AllowForSession),
                ["run_shell"] = "not-a-mode"
            },
            VerificationCommands =
            [
                new ProjectVerificationCommand { Name = "test", Command = "dotnet test" }
            ]
        });

        Assert.Equal(_tempDir, result.AgentContext.ProjectPath);
        Assert.Equal(["read_file", "write_file"], result.AgentContext.EnabledToolIds);
        Assert.Equal(ToolPermissionMode.AutoReadOnly, result.AgentContext.ToolPermissionModes["read_file"]);
        Assert.Equal(ToolPermissionMode.AllowForSession, result.AgentContext.ToolPermissionModes["write_file"]);
        Assert.False(result.AgentContext.ToolPermissionModes.ContainsKey("run_shell"));
        Assert.Equal(12, result.AgentContext.MaxToolRounds);
        Assert.True(result.AgentContext.AutoVerifyAgentRuns);
        Assert.Equal(2, result.AgentContext.MaxAutoFixRounds);
        Assert.Single(result.AgentContext.VerificationCommands);
    }

    [Fact]
    public void Build_PassesCurrentConversationInputArtifactsToAgentContext()
    {
        var conversation = CreateConversation();
        var assistant = AddMessage(conversation, ChatRole.Assistant, "placeholder");
        var factory = CreateFactory();

        var result = factory.Build(new AgentRequestBuildRequest
        {
            Conversation = conversation,
            AssistantMessageId = assistant.Id,
            EffectiveSettings = CreateEffectiveSettings(),
            RuntimeSettings = CreateRuntimeSettings(),
            ProjectPath = _tempDir,
            InputArtifacts =
            [
                new InputArtifact { Id = "current", ConversationId = conversation.Id, Summary = "current conversation", CreatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z") },
                new InputArtifact { Id = "other", ConversationId = "other-conversation", Summary = "other conversation", CreatedAt = DateTimeOffset.Parse("2026-01-03T00:00:00Z") },
                new InputArtifact { Id = "global", ConversationId = "", Summary = "project level", CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") }
            ]
        });

        Assert.Equal(["current", "global"], result.AgentContext.InputArtifacts.Select(item => item.Id));
    }

    [Fact]
    public void Build_DoesNotAttachStoredImageArtifactsWhenModelDoesNotSupportVision()
    {
        var imagePath = Path.Combine(_tempDir, "screen.png");
        File.WriteAllBytes(imagePath, [1, 2, 3, 4]);
        var conversation = CreateConversation();
        var assistant = AddMessage(conversation, ChatRole.Assistant, "placeholder");
        var factory = CreateFactory();

        var result = factory.Build(new AgentRequestBuildRequest
        {
            Conversation = conversation,
            AssistantMessageId = assistant.Id,
            EffectiveSettings = CreateEffectiveSettings(),
            RuntimeSettings = CreateRuntimeSettings(),
            ProjectPath = _tempDir,
            InputArtifacts =
            [
                new InputArtifact
                {
                    Id = "image-1",
                    ConversationId = conversation.Id,
                    Kind = InputArtifactKind.Screenshot,
                    FileName = "screen.png",
                    MimeType = "image/png",
                    CreatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
                    Metadata = { ["storedPath"] = imagePath }
                }
            ]
        });

        var userMessage = result.ChatRequest.Messages.Last(message => message.Role == ChatRole.User);
        Assert.Empty(userMessage.ContentParts);
    }

    [Fact]
    public void Build_AttachesStoredImageArtifactsWhenEffectiveSettingsSupportVision()
    {
        var imagePath = Path.Combine(_tempDir, "vision-screen.png");
        File.WriteAllBytes(imagePath, [1, 2, 3, 4]);
        var conversation = CreateConversation();
        var assistant = AddMessage(conversation, ChatRole.Assistant, "placeholder");
        var factory = CreateFactory();
        var effectiveSettings = CreateEffectiveSettings();
        effectiveSettings.ModelSupportsVision = true;

        var result = factory.Build(new AgentRequestBuildRequest
        {
            Conversation = conversation,
            AssistantMessageId = assistant.Id,
            EffectiveSettings = effectiveSettings,
            RuntimeSettings = CreateRuntimeSettings(),
            ProjectPath = _tempDir,
            InputArtifacts =
            [
                new InputArtifact
                {
                    Id = "image-1",
                    ConversationId = conversation.Id,
                    Kind = InputArtifactKind.Screenshot,
                    FileName = "screen.png",
                    MimeType = "image/png",
                    CreatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
                    Metadata = { ["storedPath"] = imagePath }
                }
            ]
        });

        var userMessage = result.ChatRequest.Messages.Last(message => message.Role == ChatRole.User);
        var imagePart = Assert.Single(userMessage.ContentParts);
        Assert.Equal("image", imagePart.Type);
        Assert.Equal("image/png", imagePart.MediaType);
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4]), imagePart.DataBase64);
        Assert.Equal(imagePath, imagePart.SourcePath);
    }

    private static AgentRequestFactory CreateFactory()
    {
        return new AgentRequestFactory(new ConversationContextBuilder(
            new SimpleContextEstimator(),
            new SystemPromptBuilder()));
    }

    private static Conversation CreateConversation()
    {
        var conversation = new Conversation { Title = "Test" };
        AddMessage(conversation, ChatRole.User, "fix bug");
        return conversation;
    }

    private static ChatMessage AddMessage(Conversation conversation, ChatRole role, string content)
    {
        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = role,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        };
        conversation.Messages.Add(message);
        return message;
    }

    private static AppSettings CreateEffectiveSettings()
    {
        return new AppSettings
        {
            ProviderId = "deepseek",
            ProtocolId = "openai",
            ProviderName = "DeepSeek",
            BaseUrl = "https://api.deepseek.com",
            Model = "deepseek-v4-pro",
            Temperature = 0.3,
            ModelContextLimit = 128_000,
            ModelParameters = new Dictionary<string, string>
            {
                ["deepseek.thinking"] = "enabled"
            }
        };
    }

    private static AppSettings CreateRuntimeSettings()
    {
        return new AppSettings
        {
            EnabledToolIds = ["read_file", "write_file"],
            ToolPermissionModes = new Dictionary<string, ToolPermissionMode>
            {
                ["read_file"] = ToolPermissionMode.AutoReadOnly,
                ["write_file"] = ToolPermissionMode.ConfirmEachTime
            },
            AgentMaxToolRounds = 12,
            AutoVerifyAgentRuns = true,
            MaxAutoFixRounds = 2
        };
    }
}
