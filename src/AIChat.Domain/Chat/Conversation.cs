namespace AIChat.Domain.Chat;

// A conversation belongs to a project workspace and owns both visible messages
// and saved provider call records.
public sealed class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "新对话";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<ChatMessage> Messages { get; set; } = [];
    // These records are not part of the model context; they exist for learning,
    // debugging, and later observability.
    public List<LlmCallDetail> CallDetails { get; set; } = [];
}
