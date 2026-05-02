namespace AIChat.Domain.Chat;

// A single streamed piece of model output. Providers translate their own
// protocol-specific streaming events into this common shape.
public sealed class ChatDelta
{
    // Text that can be appended to the assistant message in the UI.
    public string Content { get; init; } = "";
    // DeepSeek thinking mode streams reasoning_content separately from content.
    public string ReasoningContent { get; init; } = "";
    // Original provider event, kept for the call-detail inspector and debugging.
    public string RawJson { get; init; } = "";
    // Some streaming protocols send an explicit terminal event such as [DONE].
    public bool IsCompleted { get; init; }
    // Tool calls are surfaced once the provider has reconstructed name + JSON
    // arguments from the streaming response.
    public IReadOnlyList<ChatToolCall> ToolCalls { get; init; } = [];
    // HTTP status code when the provider encounters an HTTP error. Null for
    // successful responses. Used by the retry policy to detect transient errors.
    public int? HttpStatusCode { get; init; }
}
