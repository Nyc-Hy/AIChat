using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;

namespace AIChat.Abstractions.Llm;

// Provider adapter contract. Each implementation translates the common
// ChatRequest into one provider's HTTP protocol and streams ChatDelta values back.
public interface IChatProvider
{
    LlmProviderInfo Info { get; }
    bool CanHandle(AppSettings settings);
    IAsyncEnumerable<ChatDelta> SendAsync(ChatRequest request, AppSettings settings, CancellationToken cancellationToken = default);
}
