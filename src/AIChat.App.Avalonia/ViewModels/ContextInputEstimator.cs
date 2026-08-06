namespace AIChat.App.Avalonia.ViewModels;

// Small static helper for the status-bar context-meter math. Used by
// the host (MainWindowViewModel) on every project / prompt change
// for the live meter estimate, and by the agent runner at BeginRun
// to push the authoritative count once the request factory has
// produced the real context pack. Kept in the ViewModels project
// (rather than AIChat.Application) because it's purely a
// presentation-layer number — Application has no other consumer.
public static class ContextInputEstimator
{
    // Reserved for the system prompt + tool schema. Matches the
    // 1500 the previous SessionInsightsViewModel used; the number
    // isn't critical because the status bar only shows a percentage
    // against a 64K budget and the +1500 nudges the meter by ~2.3%.
    private const int SystemAndToolSchemaBudget = 1500;

    public static int Estimate(int contextTokens, string goal)
    {
        return Math.Max(0, contextTokens) + EstimateTextTokens(goal) + SystemAndToolSchemaBudget;
    }

    // Rough heuristic: ~1 token per 1.5 characters of mixed CJK/Latin
    // text. Matches the original SessionInsightsViewModel implementation
    // so behaviour is identical after the extraction.
    private static int EstimateTextTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        return Math.Max(1, (int)Math.Round(text.Length / 1.5));
    }
}
