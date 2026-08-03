using AIChat.Application.Llm.Routing;

namespace AIChat.Tests.Providers;

// 2026-08-02: catalog is now single-provider (MiniMax only). The
// deepseek / tokenplan-mimo / openai-compatible rows are gone — every
// Resolve call lands on the MiniMax coding profile (or the matching
// Default shape for unknown provider ids).
public sealed class ModelProfileCatalogTests
{
    [Theory]
    [InlineData("minimax", "MiniMax-M3", "MiniMax coding profile")]
    public void Resolve_ReturnsProviderSpecificProfile(string providerId, string modelId, string expectedName)
    {
        var profile = ModelProfileCatalog.Resolve(providerId, modelId);

        Assert.Equal(expectedName, profile.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(profile.ToolCallPolicy));
        Assert.False(string.IsNullOrWhiteSpace(profile.CacheStrategy));
    }

    [Fact]
    public void Resolve_FallsBackForUnknownProvider()
    {
        // The "Default" row now carries MiniMax's coding-shape
        // policy / cache strategy (instead of the old generic
        // OpenAI-compatible default), so an unknown-provider
        // lookup still lands on sensible MiniMax-flavored guidance
        // rather than a stripped-down generic profile.
        var profile = ModelProfileCatalog.Resolve("custom", "model");

        Assert.Contains("default", profile.DisplayName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("custom", profile.ProviderId);
        Assert.False(string.IsNullOrWhiteSpace(profile.ToolCallPolicy));
    }
}
