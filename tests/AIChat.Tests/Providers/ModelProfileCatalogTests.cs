using AIChat.Application.Llm.Routing;

namespace AIChat.Tests.Providers;

public sealed class ModelProfileCatalogTests
{
    [Theory]
    [InlineData("deepseek", "deepseek-chat", "DeepSeek coding profile")]
    [InlineData("tokenplan-mimo", "mimo-v2.5-pro", "MiMo long-context coding profile")]
    [InlineData("minimax", "MiniMax-M2.1", "MiniMAX coding profile")]
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
        var profile = ModelProfileCatalog.Resolve("custom", "model");

        Assert.Contains("default", profile.DisplayName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("custom", profile.ProviderId);
    }
}
