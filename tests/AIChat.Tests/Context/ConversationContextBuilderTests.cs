using AIChat.Abstractions.Configuration;
using AIChat.Application.Context;
using AIChat.Application.Prompting;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Context;

public sealed class ConversationContextBuilderTests
{
    [Fact]
    public void Build_AddsSystemPromptAndKeepsRecentMessagesWithinBudget()
    {
        var builder = new ConversationContextBuilder(new SimpleContextEstimator(), new SystemPromptBuilder());
        var messages = Enumerable.Range(1, 20)
            .Select(index => new ChatMessage
            {
                Role = index % 2 == 0 ? ChatRole.Assistant : ChatRole.User,
                Content = $"message-{index} " + new string('x', 120),
                CreatedAt = DateTimeOffset.Now.AddMinutes(index)
            })
            .ToList();

        var result = builder.Build(new ConversationContextBuildRequest
        {
            Messages = messages,
            Settings = new AppSettings { ModelContextLimit = 180 },
            PromptContext = new SystemPromptContext { ProjectName = "AIChat", ProjectPath = @"D:\Code\AIChat" }
        });

        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Contains("message-20", result.Last().Content);
        Assert.DoesNotContain(result, message => message.Content.StartsWith("message-1 ", StringComparison.Ordinal));
        Assert.All(result, message => Assert.NotEqual(ChatRole.Tool, message.Role));
    }

    [Fact]
    public void Build_DropsErrorAndBlankMessages()
    {
        var builder = new ConversationContextBuilder(new SimpleContextEstimator(), new SystemPromptBuilder());
        var result = builder.Build(new ConversationContextBuildRequest
        {
            Messages =
            [
                new ChatMessage { Role = ChatRole.User, Content = "keep me" },
                new ChatMessage { Role = ChatRole.Assistant, Content = "skip me", IsError = true },
                new ChatMessage { Role = ChatRole.User, Content = "   " },
                new ChatMessage { Role = ChatRole.Tool, Content = "tool result" }
            ],
            Settings = new AppSettings { ModelContextLimit = 64_000 },
            PromptContext = new SystemPromptContext()
        });

        Assert.Equal(3, result.Count);
        Assert.Contains("keep me", result[1].Content);
        Assert.Equal(ChatRole.Tool, result[2].Role);
        Assert.Contains("tool result", result[2].Content);
    }
}
