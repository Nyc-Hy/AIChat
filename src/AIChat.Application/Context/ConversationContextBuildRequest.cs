using AIChat.Abstractions.Configuration;
using AIChat.Application.Prompting;
using AIChat.Domain.Chat;

namespace AIChat.Application.Context;

public sealed class ConversationContextBuildRequest
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public required AppSettings Settings { get; init; }
    public required SystemPromptContext PromptContext { get; init; }
}
