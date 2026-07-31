using AIChat.Application.Llm.Routing;

namespace AIChat.Tests.Configuration;

// Locks the catalog's ResolveModel behavior. The critical guarantee is that
// a user-typed model id the catalog doesn't know about is preserved verbatim
// instead of silently falling back to Models.First() — which was the
// "I can't bind a model in settings" daily-driver bug. Each test below
// pins a specific aspect of that contract.
public class ChatProviderCatalogTests
{
    [Fact]
    public void ResolveModel_KnownModelId_ReturnsCatalogEntry()
    {
        var model = ChatProviderCatalog.ResolveModel("deepseek", "deepseek-chat");

        Assert.Equal("deepseek-chat", model.Id);
        // Known catalog models must keep their full capabilities, not get
        // downgraded to the synthetic ToolCapable fallback.
        Assert.True(model.Capabilities.SupportsTools);
        Assert.True(model.Capabilities.SupportsThinking);
    }

    [Fact]
    public void ResolveModel_UnknownModelId_ReturnsSyntheticEntryWithUserInput()
    {
        var model = ChatProviderCatalog.ResolveModel("deepseek", "deepseek-experimental-9000");

        Assert.Equal("deepseek-experimental-9000", model.Id);
        Assert.Equal("deepseek-experimental-9000", model.DisplayName);
        // Synthetic model gets tools capability (matches the OpenAI-compatible
        // path) and the provider's default context limit.
        Assert.True(model.Capabilities.SupportsTools);
        Assert.Equal(ChatProviderCatalog.DeepSeek.DefaultContextLimit, model.ContextLimit);
    }

    [Fact]
    public void ResolveModel_KnownIdWithSurroundingWhitespace_StillMatchesCatalog()
    {
        // Regression guard: a user typing "deepseek-chat " with a stray
        // space must still match the catalog entry (full capabilities),
        // not be downgraded to the synthetic ToolCapable fallback.
        var model = ChatProviderCatalog.ResolveModel("deepseek", "  deepseek-chat  ");

        Assert.Equal("deepseek-chat", model.Id);
        Assert.True(model.Capabilities.SupportsThinking);
    }

    [Fact]
    public void ResolveModel_EmptyModelId_FallsBackToFirstCatalogModel()
    {
        var model = ChatProviderCatalog.ResolveModel("deepseek", "");

        // Empty / whitespace modelId is the one case where the fallback
        // is the right behavior — "old settings files with an empty
        // Model field" must not crash startup. DeepSeek's first catalog
        // model is its current default.
        Assert.Equal(ChatProviderCatalog.DeepSeek.Models[0].Id, model.Id);
    }

    [Fact]
    public void ResolveModel_NullModelId_FallsBackToFirstCatalogModel()
    {
        var model = ChatProviderCatalog.ResolveModel("deepseek", null);

        Assert.Equal(ChatProviderCatalog.DeepSeek.Models[0].Id, model.Id);
    }

    [Fact]
    public void ResolveModel_UnknownIdForAnthropic_ReturnsSyntheticEntry()
    {
        // Anthropic is a different provider with a different protocol,
        // and the bug surfaced there too. The user-typed id must be
        // preserved.
        var model = ChatProviderCatalog.ResolveModel("anthropic", "claude-5-future-thing");

        Assert.Equal("claude-5-future-thing", model.Id);
        Assert.Equal(ChatProviderCatalog.Anthropic.DefaultContextLimit, model.ContextLimit);
    }
}
