using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Llm.Routing;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Providers;

public sealed class RoutedChatCompletionServiceTests
{
    [Fact]
    public async Task SendAsync_StandardizesProviderHttpErrors()
    {
        var service = new RoutedChatCompletionService(
        [
            new FakeProvider(new ChatDelta
            {
                Content = "raw failure",
                RawJson = "{\"error\":\"bad key\"}",
                HttpStatusCode = 401
            })
        ]);

        var deltas = new List<ChatDelta>();
        await foreach (var delta in service.SendAsync(
                           new ChatRequest { Model = "test", Messages = [] },
                           new AppSettings
                           {
                               ProviderName = "Provider",
                               ProviderId = "fake",
                               ProtocolId = "fake"
                           }))
        {
            deltas.Add(delta);
        }

        var error = Assert.Single(deltas);
        Assert.Equal(401, error.HttpStatusCode);
        Assert.Contains("API Key", error.Content);
        Assert.Contains("Provider", error.Content);
    }

    private sealed class FakeProvider : IChatProvider
    {
        private readonly ChatDelta _delta;

        public FakeProvider(ChatDelta delta)
        {
            _delta = delta;
        }

        public LlmProviderInfo Info { get; } = new()
        {
            Id = "fake",
            ProtocolId = "fake",
            Name = "Fake",
            DefaultBaseUrl = "https://fake.example",
            DefaultModel = "fake",
            DefaultContextLimit = 1_000
        };

        public bool CanHandle(AppSettings settings)
        {
            return true;
        }

        public async IAsyncEnumerable<ChatDelta> SendAsync(
            ChatRequest request,
            AppSettings settings,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return _delta;
        }
    }
}
