namespace AIChat.Domain.Context;

// Estimated context-window usage. It is intentionally approximate in this MVP;
// the important idea is that the UI and future Agent planner can react to limits.
public sealed class ContextUsage
{
    public int CurrentTokens { get; init; }
    public int ConversationLimit { get; init; }
    public int ModelLimit { get; init; }

    // Ratio is based on the full model limit, while the UI also shows a smaller
    // conversation budget to leave room for future system prompts and tool data.
    public double Ratio => ModelLimit <= 0 ? 0 : Math.Clamp((double)CurrentTokens / ModelLimit, 0, 1);
    public int RemainingModelTokens => Math.Max(0, ModelLimit - CurrentTokens);
}
