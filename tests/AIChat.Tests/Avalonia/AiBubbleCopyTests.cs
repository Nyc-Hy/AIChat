using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Tests.Avalonia;

// 1.0.1: tests for MainWindowViewModel.CopyAssistantBubbleAsync,
// the host-side helper the AI bubble 复制 button routes through
// (see MainWindow.axaml.cs AiBubbleCopy_OnClick). The method
// shells out to IClipboardService so a mock service can verify
// what gets sent; we also cover the empty-text + clipboard-
// unavailable branches so the code-behind handler doesn't
// have to short-circuit.
public sealed class AiBubbleCopyTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly InMemoryAppRepository _repository;
    private readonly MockClipboardService _clipboard;

    public AiBubbleCopyTests()
    {
        _repository = new InMemoryAppRepository();
        _clipboard = new MockClipboardService();
        // Two-step build: register the production
        // services first, then re-register the
        // clipboard as the LAST entry so the
        // service collection's last-wins rule
        // replaces the AppHost factory with
        // our mock. Registering the mock before
        // AppHost.Build is a no-op (the
        // factory registration at line 146 of
        // ServiceRegistration overwrites the
        // earlier instance).
        var services = new ServiceCollection();
        services.AddSingleton<IAppRepository>(_repository);
        services.AddAIChatDesktop();
        services.AddSingleton<IClipboardService>(_clipboard);
        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task Copy_NonEmptyText_SetsClipboardAndReturnsLength()
    {
        // The happy path: AI bubble has a
        // response, user clicks 复制, the
        // clipboard receives the full text and
        // the VM returns the length so the
        // click handler can build a "已复制 N
        // 字符" toast without re-counting.
        var viewModel = _provider.GetRequiredService<MainWindowViewModel>();
        var payload = "Hello there, world!"; // 19 chars

        var copied = await viewModel.CopyAssistantBubbleAsync(payload);

        Assert.Equal(payload.Length, copied);
        Assert.Equal(payload, _clipboard.LastSetText);
        Assert.Equal(1, _clipboard.SetTextCallCount);
    }

    [Fact]
    public async Task Copy_EmptyText_ReturnsZeroAndDoesNotCallClipboard()
    {
        // A defensive guard — an AI bubble that
        // somehow reaches the click handler with
        // an empty Detail (e.g. the user clicks
        // the still-thinking bubble faster than
        // the runner re-raises CanCopyAssistantBubble)
        // must NOT land an empty string on the
        // clipboard. The user would paste
        // nothing and have no idea why.
        var viewModel = _provider.GetRequiredService<MainWindowViewModel>();

        var copied = await viewModel.CopyAssistantBubbleAsync("");

        Assert.Equal(0, copied);
        Assert.Null(_clipboard.LastSetText);
    }

    [Fact]
    public async Task Copy_WhitespaceOnlyText_ReturnsZero()
    {
        // Whitespace-only is the same trap as
        // empty — pasting " " would be silent
        // to the user.
        var viewModel = _provider.GetRequiredService<MainWindowViewModel>();

        var copied = await viewModel.CopyAssistantBubbleAsync("   \n  \t");

        Assert.Equal(0, copied);
        Assert.Null(_clipboard.LastSetText);
    }

    [Fact]
    public async Task Copy_NullText_ReturnsZero()
    {
        // The XAML's Tag binding is a string
        // (Detail), so null only shows up if a
        // refactor changes the binding to a
        // nullable source. The guard is cheap
        // and keeps the click handler
        // crash-free.
        var viewModel = _provider.GetRequiredService<MainWindowViewModel>();

        var copied = await viewModel.CopyAssistantBubbleAsync(null);

        Assert.Equal(0, copied);
        Assert.Null(_clipboard.LastSetText);
    }

    [Fact]
    public async Task Copy_LongMarkdownResponse_SetsFullString()
    {
        // A 4KB markdown response (headings +
        // code blocks + lists) should be copied
        // verbatim — no truncation, no markdown
        // stripping. The user wants the same
        // text they see in the bubble, ready to
        // paste into a code review or a Slack
        // thread.
        var viewModel = _provider.GetRequiredService<MainWindowViewModel>();
        var payload = new string('A', 4000) + "\n## Heading\n- item 1\n- item 2\n```\ncode\n```";

        var copied = await viewModel.CopyAssistantBubbleAsync(payload);

        Assert.Equal(payload.Length, copied);
        Assert.Equal(payload, _clipboard.LastSetText);
    }
}
