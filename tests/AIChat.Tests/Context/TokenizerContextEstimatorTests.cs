using AIChat.Abstractions.Configuration;
using AIChat.Application.Context;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Context;

public sealed class TokenizerContextEstimatorTests
{
    [Fact]
    public void Estimate_ReturnsReasonableTokenCount()
    {
        var estimator = new TokenizerContextEstimator();
        var settings = new AppSettings { ModelContextLimit = 128_000 };

        var messages = new List<ChatMessage>
        {
            new() { Role = ChatRole.User, Content = "Hello, how are you?" },
            new() { Role = ChatRole.Assistant, Content = "I'm doing well, thank you for asking! How can I help you today?" }
        };

        var usage = estimator.Estimate(messages, settings);

        // "Hello, how are you?" ≈ 6 tokens, "I'm doing well..." ≈ 16 tokens, + overhead
        Assert.InRange(usage.CurrentTokens, 10, 50);
        Assert.Equal(128_000, usage.ModelLimit);
        Assert.Equal((int)(128_000 * 0.7), usage.ConversationLimit);
    }

    [Fact]
    public void Estimate_EmptyMessages_ReturnsMinimalTokens()
    {
        var estimator = new TokenizerContextEstimator();
        var settings = new AppSettings { ModelContextLimit = 128_000 };

        var usage = estimator.Estimate([], settings);

        Assert.InRange(usage.CurrentTokens, 0, 5);
    }

    [Fact]
    public void Estimate_ChineseText_HasReasonableCount()
    {
        var estimator = new TokenizerContextEstimator();
        var settings = new AppSettings { ModelContextLimit = 128_000 };

        var messages = new List<ChatMessage>
        {
            new() { Role = ChatRole.User, Content = "你好，请帮我写一个 Hello World 程序。" }
        };

        var usage = estimator.Estimate(messages, settings);

        // Chinese text uses more tokens per character than English
        Assert.InRange(usage.CurrentTokens, 5, 40);
    }

    [Fact]
    public void Estimate_LargeContextLimit_Uses70Percent()
    {
        var estimator = new TokenizerContextEstimator();
        var settings = new AppSettings { ModelContextLimit = 1_000_000 };

        var messages = new List<ChatMessage>
        {
            new() { Role = ChatRole.User, Content = "test" }
        };

        var usage = estimator.Estimate(messages, settings);

        Assert.Equal(700_000, usage.ConversationLimit);
    }
}
