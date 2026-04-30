using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;

namespace AIChat.Application.Llm.Routing;

// Chooses the concrete provider adapter for a request. This is the first small
// "router" pattern you will reuse in Agents: caller asks for a capability, the
// router picks the implementation that can handle the current settings.
public sealed class RoutedChatCompletionService : IChatCompletionService
{
    private readonly IReadOnlyList<IChatProvider> _providers;

    public RoutedChatCompletionService(IEnumerable<IChatProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IAsyncEnumerable<ChatDelta> SendAsync(ChatRequest request, AppSettings settings, CancellationToken cancellationToken = default)
    {
        // Providers decide if they can handle the settings by protocol/provider
        // metadata. If nothing matches, fall back to the first registered provider.
        var provider = _providers.FirstOrDefault(item => item.CanHandle(settings)) ?? _providers.First();
        return provider.SendAsync(request, settings, cancellationToken);
    }
}
