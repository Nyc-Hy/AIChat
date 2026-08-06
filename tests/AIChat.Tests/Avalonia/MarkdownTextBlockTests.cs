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
    public void Render_NumberedList_KeepsUserNumberedMarker()
    {
        // 1. text → "1. text" verbatim
        // (number + period + space). The
        // user-typed number is kept so
        // non-sequential lists ("1. then
        // 3. then 5.") keep their
        // meaning — auto-renumbering
        // would silently change the
        // agent's intent.
        var text = RenderText("1. first\n2. second\n3. third");
        Assert.Equal("1. first\n2. second\n3. third", text);
    }

    [Fact]
    public void Render_NumberedList_NonSequential_PreservesGaps()
    {
        // "1. then 3." stays "1. then
        // 3." — the user (or agent)
        // explicitly skipped 2, and the
        // renderer must not silently
        // re-number.
        var text = RenderText("1. first\n3. third");
        Assert.Equal("1. first\n3. third", text);
    }

    [Fact]
    public void Render_NumberedList_IndentedNumberedList_PreservesIndent()
    {
        // 2-space indent before the
        // marker, like a nested list.
        // The indent is preserved in the
        // rendered text (the visual
        // hierarchy comes from the
        // leading spaces, just like the
        // bullet path).
        var text = RenderText("  1. nested");
        Assert.Equal("  1. nested", text);
    }

    [Fact]
    public void Render_NumberedList_BodyHasInlineElements_RendersThem()
    {
        // The numbered list body is
        // routed through the inline
        // parser, so inline code /
        // bold / italic inside a list
        // item still get the same
        // styling as in a plain
        // paragraph. The test pins
        // both the marker preservation
        // and the inline rendering.
        var text = RenderText("1. run `dotnet test`");
        Assert.Equal("1. run dotnet test", text);
    }

    [Fact]
    public void Render_InlineItalic_SingleAsterisk()
    {
        // *italic* → italic Run. The
        // lookarounds in the pattern
        // ensure the * is not part of
        // a ** pair, so **bold** still
        // wins (verified by the
        // Render_BoldAndItalicTogether
        // test).
        var text = RenderText("this is *italic* text");
        Assert.Equal("this is italic text", text);
    }

    [Fact]
    public void Render_InlineItalic_Underscore()
    {
        // _italic_ → italic Run. The
        // word-boundary lookarounds
        // keep var_name (where _ sits
        // between word characters) from
        // being mistaken for italic
        // markers.
        var text = RenderText("this is _italic_ text");
        Assert.Equal("this is italic text", text);
    }

    [Fact]
    public void Render_InlineItalic_DoesNotEatSnakeCaseIdentifier()
    {
        // var_name has _ between two
        // word characters — the
        // word-boundary lookarounds
        // (?![\w] / (?<![\w]) on the
        // underscore alternative)
        // must keep the renderer from
        // splitting it as `var` +
        // italic(`name`). The visible
        // result is the verbatim text.
        var text = RenderText("use var_name here");
        Assert.Equal("use var_name here", text);
    }

    [Fact]
    public void Render_InlineBoldAndItalic_DoNotConflict()
    {
        // The single-* italic lookarounds
        // keep it from eating one half
        // of the **bold** pair. The
        // **bold** match must still
        // happen first; the *italic*
        // match only fires on its own
        // *…* runs.
        var text = RenderText("**bold** and *italic*");
        Assert.Equal("bold and italic", text);
    }

    [Fact]
    public void Render_InlineLink_BoldsTextAndAppendsDimmedUrl()
    {
        // [text](url) renders as "text"
        // (the link text, bolded) +
        // " (url)" (the URL in the
        // muted brush, in parens). The
        // user can read the URL and
        // copy it via the existing
        // 复制 button or by drag-
        // selecting the dimmed run.
        // Flattened, the visible text
        // is "text (url)" — bold
        // styling on the link text is
        // visual, the test only pins
        // the character content.
        var text = RenderText("see [the docs](https://example.com)");
        Assert.Equal("see the docs (https://example.com)", text);
    }

    [Fact]
    public void Render_InlineLink_LinkTextStillPicksUpInlineElements()
    {
        // The link text is rendered
        // through the same inline
        // path as plain text, so
        // [text with **bold**](url)
        // bolds inside the link text
        // as well. Flattened: "link
        // text (url)" — the bold is
        // a visual property of the
        // text run, not reflected in
        // the test assertion.
        var text = RenderText("[**link text**](https://example.com)");
        Assert.Equal("link text (https://example.com)", text);
    }

    [Fact]
    public void Render_InlineLink_InsideParagraph()
    {
        // A paragraph that mixes
        // prose, a link, and trailing
        // prose — common agent output
        // shape ("see [the API
        // docs](https://...) for
        // details"). All the inline
        // elements on either side of
        // the link should still come
        // through.
        var text = RenderText("see [the docs](https://example.com) for details");
        Assert.Equal("see the docs (https://example.com) for details", text);
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
