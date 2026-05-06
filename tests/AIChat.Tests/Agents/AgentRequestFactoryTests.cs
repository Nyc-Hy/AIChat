using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents;
using AIChat.Application.Context;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;
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
            WorkspaceChangeCount = 2
        });

        Assert.Equal("deepseek-v4-pro", result.ChatRequest.Model);
        Assert.Equal(0.3, result.ChatRequest.Temperature);
        Assert.Equal(_tempDir, result.FileIndex.RootPath);
        Assert.Contains(result.FileIndex.Entries, entry => entry.RelativePath == "Program.cs");
        Assert.Contains("分支：main，未提交变更：2 个文件", result.WorkspaceSummary);
        Assert.Equal(ChatRole.System, result.ChatRequest.Messages[0].Role);
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
