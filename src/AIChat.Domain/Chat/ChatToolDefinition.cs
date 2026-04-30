namespace AIChat.Domain.Chat;

// Provider-neutral tool schema. Providers map this to their own function/tool
// declaration format when making a model request.
public sealed class ChatToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ParametersJson { get; init; }
}
