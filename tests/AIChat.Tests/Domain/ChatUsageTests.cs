using AIChat.Domain.Chat;

namespace AIChat.Tests.Domain;

// 2026-08-05: pin the math on ChatUsage.CacheHitPercent
// so a future refactor that loses the percentage formatting
// (e.g. flipping to "0.x fraction" instead of "0–100 percent")
// breaks here rather than as a daily-driver "the cache hit
// says -0.0% in the activity feed" support ticket.
public class ChatUsageTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(100, 0, 0, 0.0)]
    [InlineData(100, 50, 50, 50.0)]
    [InlineData(177, 114, 5, 64.4)]
    [InlineData(1000, 1000, 0, 100.0)]
    public void CacheHitPercent_ComputesFraction(int prompt, int cached, int completion, double expected)
    {
        var usage = new ChatUsage { PromptTokens = prompt, CachedTokens = cached, CompletionTokens = completion };

        Assert.Equal(expected, usage.CacheHitPercent);
    }

    [Fact]
    public void CacheHitPercent_NoPromptTokens_ReturnsZero()
    {
        // 0-prompt case (empty conversation) — the
        // percentage math would be 0/0 = NaN. The
        // accessor must return 0.0 explicitly so
        // XAML bindings don't display "NaN%" or
        // crash the StringFormat converter.
        var usage = new ChatUsage { PromptTokens = 0, CachedTokens = 0, CompletionTokens = 5 };

        Assert.Equal(0.0, usage.CacheHitPercent);
    }

    [Fact]
    public void TotalTokens_SumsPromptAndCompletion()
    {
        var usage = new ChatUsage { PromptTokens = 100, CompletionTokens = 42, CachedTokens = 50 };

        Assert.Equal(142, usage.TotalTokens);
    }
}
