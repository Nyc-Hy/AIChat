namespace AIChat.Domain.Chat;

// A provider-requested tool invocation. OpenAI-compatible APIs call this a
// tool_call/function call; the Agent layer executes it and feeds the result back.
public sealed class ChatToolCall
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string ArgumentsJson { get; set; } = "{}";
}
