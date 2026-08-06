using AIChat.App.Avalonia.ViewModels;

namespace AIChat.Tests.Avalonia;

// Unit tests for the input-tokens math that drives the status-bar
// context meter. Host (MainWindowViewModel) uses the Estimate method
// on every project / prompt / no-write change to keep the meter
// current, and the agent runner calls it at BeginRun with the
// authoritative requestFactory.ContextPack.EstimatedTokens so the
// meter reflects the actual request the agent will send. Both call
// sites go through the same formula, so the test pins the
// behaviour the meter depends on.
public class ContextInputEstimatorTests
{
    [Fact]
    public void Estimate_EmptyContext_AddsPromptAndSystemBudget()
    {
        // 0 context + 0 prompt + 1500 system/tool schema budget = 1500.
        // The +1500 nudges the meter by ~2.3% of the 64K window,
        // not critical but it represents the system prompt + tool
        // schema overhead that always travels with a request.
        Assert.Equal(1500, ContextInputEstimator.Estimate(0, ""));
    }

    [Fact]
    public void Estimate_NonZeroContext_AddsAllThreeBuckets()
    {
        // 1000 context + 27-char prompt (round(27 / 1.5) = 18 tokens)
        // + 1500 system budget = 2518.
        var tokens = ContextInputEstimator.Estimate(1000, "abc def ghi jkl mno pqr stu");
        Assert.Equal(2518, tokens);
    }

    [Fact]
    public void Estimate_NegativeContext_TreatedAsZero()
    {
        // Defensive: the runner passes requestBuild.ContextPack?.EstimatedTokens
        // ?? 0, but a stale persisted run could in theory have a negative
        // value if the schema ever changes. The status bar shouldn't go
        // negative just because a stored number did.
        Assert.Equal(1500, ContextInputEstimator.Estimate(-100, ""));
    }

    [Fact]
    public void Estimate_LongPrompt_GrowsRoughlyOnePointFiveCharsPerToken()
    {
        // The heuristic is "1 token per 1.5 characters of mixed
        // CJK/Latin text", matching the previous
        // SessionInsightsViewModel.EstimateTextTokens. A 300-char
        // prompt should land at round(300 / 1.5) = 200 tokens
        // for the prompt itself, + 1500 system budget = 1700 total.
        var tokens = ContextInputEstimator.Estimate(0, new string('a', 300));
        Assert.Equal(1700, tokens);
    }
}
