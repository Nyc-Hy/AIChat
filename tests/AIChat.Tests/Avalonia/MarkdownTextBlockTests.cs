using AIChat.App.Avalonia.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;

namespace AIChat.Tests.Avalonia;

// 2026-08-05: tests for the expanded markdown
// surface. The previous version only handled
// inline code + bold; agent output is mostly
// fenced code blocks + headers, so the parser
// is now a small line-oriented state machine
// over the markdown text. These tests pin each
// block-level element so a future refactor
// can't regress the daily-driver rendering.
//
// Avalonia is process-wide — only one
// AppBuilder.Configure() can succeed per
// process. IClassFixture<AvaloniaHeadlessFixture>
// re-runs the ctor per test class on whatever
// thread xunit chose, and the second init throws
// "different thread owns it". The static lock
// below ensures the init runs exactly once,
// regardless of which class triggered the
// first call. Subsequent test classes see
// `Initialised` already true and skip the
// AppBuilder call entirely.
public class MarkdownTextBlockTests
{
    private static readonly object _initLock = new();
    private static bool _avalaniaInitialised;

    private static void EnsureAvalonia()
    {
        if (_avalaniaInitialised)
        {
            return;
        }
        lock (_initLock)
        {
            if (_avalaniaInitialised)
            {
                return;
            }
            AppBuilder.Configure<AvaloniaHeadlessApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions
                {
                    UseHeadlessDrawing = false
                })
                .SetupWithoutStarting();
            _avalaniaInitialised = true;
        }
    }

    // Empty Avalonia.Application subclass
    // that satisfies AppBuilder.Configure<T>().
    // Stays private to this file — no other
    // test needs it.
    private sealed class AvaloniaHeadlessApp : global::Avalonia.Application
    {
    }

    private static string RenderText(string markdown)
    {
        // The OnPropertyChanged handler
        // runs synchronously when the
        // Markdown styled property is
        // set — it clears + repopulates
        // Inlines in the same call, so
        // a Measure() pass is unnecessary
        // for verifying the parser
        // output. Skipping Measure keeps
        // the test free of Avalonia
        // platform init (no Skia, no
        // dispatcher, no font manager
        // lookup) which means the test
        // runs in any test class
        // collection without deadlocking
        // the parallel runner.
        var block = new MarkdownTextBlock { Markdown = markdown };
        return string.Concat(block.Inlines!.Select(inline => InlineToText(inline)));
    }

    private static string InlineToText(Inline inline) => inline switch
    {
        Run run => run.Text ?? "",
        LineBreak => "\n",
        Bold bold => string.Concat(bold.Inlines!.Select(InlineToText)),
        _ => ""
    };

    [Fact]
    public void Render_PlainText_NoTransformation()
    {
        var text = RenderText("Hello world.");
        Assert.Equal("Hello world.", text);
    }

    [Fact]
    public void Render_InlineCode_RendersAsCode()
    {
        // Inline code: `code` → the
        // monospace + code-bg Run.
        // We can't easily inspect the Run's
        // properties from outside, but we can
        // verify the text was kept verbatim.
        var text = RenderText("Use `dotnet test` to run the tests.");
        Assert.Equal("Use dotnet test to run the tests.", text);
    }

    [Fact]
    public void Render_BoldText_KeepsTextVerbatim()
    {
        // Bold: **text** → the Bold
        // container wraps a Run with the
        // text. Flattened, the text is the
        // same — the visual difference is
        // the font weight, which we don't
        // test here.
        var text = RenderText("This is **bold** text.");
        Assert.Equal("This is bold text.", text);
    }

    [Fact]
    public void Render_FencedCodeBlock_KeepsNewlinesAndText()
    {
        // The most common agent output:
        // a fenced code block. The block
        // must keep its internal newlines
        // (one per line) and the leading
        // ``` markers must NOT leak into
        // the rendered text.
        var text = RenderText(
            "Here's the fix:\n" +
            "```csharp\n" +
            "var x = 1;\n" +
            "var y = 2;\n" +
            "```\n" +
            "That should work.");
        Assert.Equal("Here's the fix:\nvar x = 1;\nvar y = 2;\nThat should work.", text);
    }

    [Fact]
    public void Render_FencedCodeBlock_NoLanguageTag_RendersCorrectly()
    {
        // ``` without a language tag.
        var text = RenderText("```\nsome content\n```");
        Assert.Equal("some content", text);
    }

    [Fact]
    public void Render_HeaderLevel1_KeepsTextVerbatim()
    {
        // The visual hierarchy is a font
        // size + weight; the test pins the
        // text so a future refactor can't
        // accidentally strip the heading
        // text.
        var text = RenderText("# Big Section");
        Assert.Equal("Big Section", text);
    }

    [Fact]
    public void Render_HeaderLevel2And3_KeepTextVerbatim()
    {
        var text = RenderText("## Medium\n### Small");
        Assert.Equal("Medium\nSmall", text);
    }

    [Fact]
    public void Render_HeaderWithTrailingHashes_StripsClosingHashes()
    {
        // Markdown allows trailing
        // optional closing #s on
        // headers. The parser strips
        // them so the rendered text
        // doesn't show the noise.
        var text = RenderText("## Section ##");
        Assert.Equal("Section", text);
    }

    [Fact]
    public void Render_Blockquote_KeepsTextAndAddsBar()
    {
        // The blockquote visual is "▎ "
        // followed by the text in italic.
        // We don't assert italic / color
        // (those are visual), but the
        // text content + the bar must
        // both be present.
        var text = RenderText("> quoted text");
        Assert.Equal("▎ quoted text", text);
    }

    [Fact]
    public void Render_BulletList_ReplacesMarkerWithBullet()
    {
        // - item → "• item" prefix.
        // The leading whitespace (if any)
        // is preserved for indented lists.
        var text = RenderText("- first\n- second");
        Assert.Equal("• first\n• second", text);
    }

    [Fact]
    public void Render_AsteriskBullet_ReplacesWithBullet()
    {
        var text = RenderText("* first\n* second");
        Assert.Equal("• first\n• second", text);
    }

    [Fact]
    public void Render_BlankLineBetweenParagraphs_BreaksParagraph()
    {
        // The block-level state machine
        // emits a LineBreak when it sees a
        // blank line so the two paragraphs
        // render as separate lines.
        var text = RenderText("First paragraph.\n\nSecond paragraph.");
        Assert.Equal("First paragraph.\nSecond paragraph.", text);
    }

    [Fact]
    public void Render_MixedMarkdownAllElementsTogether()
    {
        // The "kitchen sink" — the model
        // can produce any of these
        // elements in a single response.
        // The test pins the rendered text
        // so a refactor of the
        // line-oriented state machine
        // can't regress the most common
        // shape.
        var markdown =
            "Here's what I changed:\n" +
            "\n" +
            "## Summary\n" +
            "\n" +
            "I added a new helper:\n" +
            "```csharp\n" +
            "static int Add(int a, int b) => a + b;\n" +
            "```\n" +
            "\n" +
            "Then I called it with `Add(1, 2)`.\n" +
            "\n" +
            "> Note: this is just a demo.\n" +
            "\n" +
            "- first item\n" +
            "- second item";
        var expected =
            "Here's what I changed:\n" +
            "Summary\n" +
            "I added a new helper:\n" +
            "static int Add(int a, int b) => a + b;\n" +
            "Then I called it with Add(1, 2).\n" +
            "▎ Note: this is just a demo.\n" +
            "• first item\n" +
            "• second item";
        var text = RenderText(markdown);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Render_NullOrEmptyMarkdown_ProducesNoInlines()
    {
        // Defensive: the AI bubble
        // initially has an empty Detail
        // (the "正在启动任务..." status
        // is shown via the 3-dot
        // placeholder, not via the
        // markdown body). The parser
        // must not throw on null or
        // empty input.
        var block = new MarkdownTextBlock { Markdown = null };
        Assert.Empty(block.Inlines!);

        block.Markdown = "";
        Assert.Empty(block.Inlines!);
    }
}
