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
        if (_providers.Count == 0)
        {
            throw new InvalidOperationException("No chat providers are registered.");
        }
    }

    public IAsyncEnumerable<ChatDelta> SendAsync(ChatRequest request, AppSettings settings, CancellationToken cancellationToken = default)
    {
        // A mismatched adapter can send provider-specific headers and credentials
        // to the wrong endpoint. Treat stale/unknown settings as configuration
        // errors instead of silently falling back to registration order.
        var provider = _providers.FirstOrDefault(item => item.CanHandle(settings))
                       ?? throw new InvalidOperationException(
                           $"No chat provider can handle provider '{settings.ProviderId}' " +
                           $"with protocol '{settings.ProtocolId}'.");
        return SendWithStandardizedErrorsAsync(provider, request, settings, cancellationToken);
    }

    private static async IAsyncEnumerable<ChatDelta> SendWithStandardizedErrorsAsync(
        IChatProvider provider,
        ChatRequest request,
        AppSettings settings,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var delta in provider.SendAsync(request, settings, cancellationToken))
        {
            if (delta.HttpStatusCode is > 0)
            {
                var error = ProviderErrorClassifier.FromDelta(
                    delta.HttpStatusCode,
                    settings.ProviderName,
                    string.IsNullOrWhiteSpace(delta.RawJson) ? delta.Content : delta.RawJson);
                yield return new ChatDelta
                {
                    Content = error.Message,
                    RawJson = delta.RawJson,
                    HttpStatusCode = delta.HttpStatusCode
                };
                continue;
            }

            yield return delta;
        }
    }
}
