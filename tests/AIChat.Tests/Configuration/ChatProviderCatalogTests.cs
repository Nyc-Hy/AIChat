using AIChat.Application.Llm.Routing;

namespace AIChat.Tests.Configuration;

// Locks the catalog's ResolveModel behavior. The critical guarantee is that
// a user-typed model id the catalog doesn't know about is preserved verbatim
// instead of silently falling back to Models.First() — which was the
// "I can't bind a model in settings" daily-driver bug. Each test below
// pins a specific aspect of that contract.
//
// 2026-08-02: catalog is single-provider (MiniMax only). The previous
// "KnownModelId" / "KnownIdWithSurroundingWhitespace" / "EmptyModelId" /
// "NullModelId" tests ran against the DeepSeek provider's Models list.
// They now run against the MiniMax provider's list, which carries the
// same M3 model the daily driver uses. The unknown-id case (with the
// synthetic ToolCapable fallback) and the unknown-provider fallback
// (catalog's `Resolve` collapses every unknown id to MiniMax) are still
// pinned below.
public class ChatProviderCatalogTests
{
    [Fact]
    public void ResolveModel_KnownModelId_ReturnsCatalogEntry()
    {
        var model = ChatProviderCatalog.ResolveModel("minimax", "MiniMax-M3");

        Assert.Equal("MiniMax-M3", model.Id);
        // Known catalog models must keep their full capabilities, not get
        // downgraded to the synthetic ToolCapable fallback.
        Assert.True(model.Capabilities.SupportsTools);
        Assert.True(model.Capabilities.SupportsInterleavedThinking);
    }

    [Fact]
    public void ResolveModel_UnknownModelId_ReturnsSyntheticEntryWithUserInput()
    {
        // Self-hosted MiniMax endpoint with a private model id —
        // the catalog is a defaults source, not a gate.
        var model = ChatProviderCatalog.ResolveModel("minimax", "private-cluster-2026-08");

        Assert.Equal("private-cluster-2026-08", model.Id);
        Assert.Equal("private-cluster-2026-08", model.DisplayName);
        // Synthetic model gets tools capability (matches the OpenAI-compatible
        // path) and the provider's default context limit.
        Assert.True(model.Capabilities.SupportsTools);
        Assert.Equal(ChatProviderCatalog.MiniMax.DefaultContextLimit, model.ContextLimit);
    }

    [Fact]
    public void ResolveModel_KnownIdWithSurroundingWhitespace_StillMatchesCatalog()
    {
        // Regression guard: a user typing "MiniMax-M3 " with a stray
        // space must still match the catalog entry (full capabilities),
        // not be downgraded to the synthetic ToolCapable fallback.
        var model = ChatProviderCatalog.ResolveModel("minimax", "  MiniMax-M3  ");

        Assert.Equal("MiniMax-M3", model.Id);
        Assert.True(model.Capabilities.SupportsInterleavedThinking);
    }

    [Fact]
    public void ResolveModel_EmptyModelId_FallsBackToFirstCatalogModel()
    {
        var model = ChatProviderCatalog.ResolveModel("minimax", "");

        // Empty / whitespace modelId is the one case where the fallback
        // is the right behavior — "old settings files with an empty
        // Model field" must not crash startup. MiniMax's first
        // (and only) catalog model is its current default.
        Assert.Equal(ChatProviderCatalog.MiniMax.Models[0].Id, model.Id);
    }

    [Fact]
    public void ResolveModel_NullModelId_FallsBackToFirstCatalogModel()
    {
        var model = ChatProviderCatalog.ResolveModel("minimax", null);

        Assert.Equal(ChatProviderCatalog.MiniMax.Models[0].Id, model.Id);
    }

    [Fact]
    public void ResolveModel_UnknownIdForUnknownProvider_ResolvesViaCatalogFallback()
    {
        // The previous "UnknownIdForAnthropic" case pinned that a
        // non-empty user-typed id was preserved on a different
        // protocol (Anthropic). After the 2026-08-02 prune, every
        // unknown provider id (no Anthropic, no DeepSeek) falls back
        // to MiniMax through Resolve(), but the user-typed model id
        // is still preserved verbatim — that's the contract that
        // matters for the daily-driver flow.
        var model = ChatProviderCatalog.ResolveModel("anthropic", "claude-5-future-thing");

        Assert.Equal("claude-5-future-thing", model.Id);
        Assert.Equal(ChatProviderCatalog.MiniMax.DefaultContextLimit, model.ContextLimit);
    }
}
