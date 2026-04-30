using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;

namespace AIChat.Abstractions.Llm;

// Application-facing chat service. The UI talks to this one interface instead
// of knowing which concrete provider will be called.
public interface IChatCompletionService
{
    IAsyncEnumerable<ChatDelta> SendAsync(ChatRequest request, AppSettings settings, CancellationToken cancellationToken = default);
}
