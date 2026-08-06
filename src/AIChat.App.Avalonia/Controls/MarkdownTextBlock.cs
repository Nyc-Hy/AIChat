using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaApplication = Avalonia.Application;

namespace AIChat.App.Avalonia.Controls;

// 2026-08-05: expanded markdown surface. The
// previous version only handled inline code
// (``code``) and bold (**bold**) — fine for
// short Q&A but useless for agent output, which
// is mostly fenced code blocks, headers, and
// the occasional list. The TextBlock-based
// shape stays (avoids swapping the control
// hierarchy) and now renders:
//   - ``` fenced code blocks (monospace + code-bg)
//   - # / ## / ### headers (size hierarchy)
//   - > blockquotes (italic + accent brush)
//   - - / * list bullets (• prefix, was already
//     there via NormalizeMarkdown — kept)
//   - 1. / 2. / 3. numbered lists (marker kept
//     verbatim; user types the number so a
//     1. then 3. is a 1. then 3., not 1. then 2.)
//   - inline code + bold (unchanged)
//   - inline italic: *text* and _text_
//   - inline link: [text](url) → text (bold)
//     + (url) dim, no click (TextBlock can't
//     route a click back to a view-model)
//
// Trade-off: the control is still a TextBlock,
// so code blocks render as a single Run with a
// font + background but no copy button or
// language label. A future iteration could
// swap the parent from Border/StackPanel to a
// richer ItemTemplate that hosts a copy
// button per block; for now the user can
// select + copy via the existing 复制 button on
// the AI bubble (the full Detail string is in
// the clipboard) or by mouse-drag selection.
// Same constraint for links — the link text is
// bolded so the user knows it's a link, and the
// URL is dimmed so the user can read it (and
// copy it via the same drag-select affordance),
// but clicking the link does nothing. A future
// iteration that swaps the parent control
// would let us wire actual navigation.
public sealed class MarkdownTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string?>(nameof(Markdown));

    // Inline-code background — read from the design-token
    // dictionary (CodeBgBrush) so the same hex never escapes
    // the Tokens files. Lazily resolved on first use so
    // MarkdownTextBlock can be instantiated before the
    // application resource dictionary is fully merged (e.g. in
    // design-time / unit-test hosts); falls back to a
    // transparent brush if the token isn't present.
    private static readonly Lazy<IBrush> InlineCodeBackground = new(() =>
    {
        if (AvaloniaApplication.Current?.Resources.TryGetResource("CodeBgBrush", null, out var resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }
        return Brushes.Transparent;
    });

    // Blockquote left bar + body text — the
    // blockquote text is italic; the left
    // border is rendered as a 3px-wide solid
    // line on the parent (see AddBlockquote)
    // because TextBlock doesn't support
    // left-border styling on a per-Inline
    // basis. The brush is the accent so the
    // user gets a subtle visual cue that
    // distinguishes the quote from the
    // normal text without screaming.
    private static readonly Lazy<IBrush> BlockquoteBrush = new(() =>
    {
        if (AvaloniaApplication.Current?.Resources.TryGetResource("AccentBrush", null, out var resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }
        return Brushes.Gray;
    });

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    // 2026-08-06: single regex for the
    // inline elements. Alternatives are
    // tried left-to-right, so the more
    // specific patterns (link, code,
    // **bold**) win over the more
    // permissive ones (single-*
    // italic, _italic_) at the same
    // position. The single-* italic
    // pattern is `(?<!\*)\*(?!\*)…` —
    // the lookarounds ensure the `*`
    // is not part of a `**` pair, so
    // `**bold**` is always consumed by
    // the bold alternative first. The
    // _italic_ pattern uses word-
    // boundary lookarounds so
    // `var_name` (where `_` is between
    // word characters) is not
    // mistakenly italicised — the user
    // would otherwise see their
    // identifiers rendered in italic
    // on every agent response that
    // mentions a private field.
    private static readonly Regex InlinePattern = new(
        @"(?<link>\[(?<linkText>[^\]]+)\]\((?<linkUrl>[^)]+)\))" +
        @"|`(?<code>[^`]+)`" +
        @"|\*\*(?<bold>[^*]+)\*\*" +
        @"|(?<!\*)\*(?<italicA>[^*\s][^*]*?)\*(?!\*)" +
        @"|(?<![\w])_(?<italicU>[^_\s][^_]*?)_(?![\w])",
        RegexOptions.Compiled);

    // 2026-08-06: numbered list line
    // detection. Matches "1. text",
    // "  2. text" (leading indent kept
    // as group 1 so nested numbered
    // lists get a visual indent like
    // the bullet path). The number
    // (group 2) is kept verbatim —
    // agents don't always number
    // sequentially and a 1. then 3.
    // is meaningful (e.g. "step 3
    // depends on step 1 but the
    // intermediate steps are out of
    // scope"). Auto-renumbering would
    // silently change the meaning.
    private static readonly Regex NumberedListPattern = new(
        @"^(\s*)(\d+)\.\s+(.*)$",
        RegexOptions.Compiled);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty)
        {
            RenderMarkdown(change.NewValue as string);
        }
    }

    private void RenderMarkdown(string? markdown)
    {
        Inlines?.Clear();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        // 2026-08-05: paragraph-aware
        // rendering. Block-level elements
        // (fenced code, headers, blockquotes,
        // list bullets) consume their full
        // line range; only the "remaining
        // text" lines are routed through
        // AppendInlineMarkdown. This is a
        // tiny state machine over the line
        // index, not a real parser — enough
        // to cover the 90% case for
        // agent-output markdown (which is
        // mostly prose + code blocks + the
        // occasional header / quote).
        var i = 0;
        var firstInBlock = true;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                // Blank line = paragraph
                // break. Render an empty
                // run so the next paragraph
                // starts on a new line.
                if (!firstInBlock)
                {
                    Inlines?.Add(new LineBreak());
                }
                i++;
                firstInBlock = true;
                continue;
            }
            // Fenced code block: opens with
            // ``` (optionally followed by a
            // language tag) and closes with
            // a line that's just ```.
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var codeStart = i + 1;
                var codeEnd = lines.Length;
                for (var j = codeStart; j < lines.Length; j++)
                {
                    if (lines[j].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        codeEnd = j;
                        break;
                    }
                }
                if (!firstInBlock)
                {
                    Inlines?.Add(new LineBreak());
                }
                AddCodeBlock(lines, codeStart, codeEnd);
                i = codeEnd + 1;
                firstInBlock = false;
                continue;
            }
            // ATX header: # / ## / ### at the
            // start of the line. Renders as a
            // bigger bold Run; the trailing
            // optional closing #s are stripped.
            var headerMatch = Regex.Match(line, @"^(#{1,3})\s+(.+?)\s*#*\s*$");
            if (headerMatch.Success)
            {
                if (!firstInBlock)
                {
                    Inlines?.Add(new LineBreak());
                }
                AddHeader(headerMatch.Groups[1].Value.Length, headerMatch.Groups[2].Value);
                i++;
                firstInBlock = false;
                continue;
            }
            // Blockquote: a line that
            // starts with "> " (or just
            // ">"). Renders italic + a
            // subtle 3px accent left bar
            // (the bar is the trickier part
            // — see below).
            if (line.StartsWith("> ", StringComparison.Ordinal) || line == ">")
            {
                if (!firstInBlock)
                {
                    Inlines?.Add(new LineBreak());
                }
                var quoteText = line == ">" ? "" : line[2..];
                AddBlockquote(quoteText);
                i++;
                firstInBlock = false;
                continue;
            }
            // List bullet: "- " or "* "
            // at the start of the line
            // (after optional indent). The
            // bullet is rendered as a
            // "• " prefix so it doesn't
            // get stripped of indent
            // (which a real <ul> would do).
            var bulletMatch = Regex.Match(line, @"^(\s*)[-*]\s+(.*)$");
            if (bulletMatch.Success)
            {
                if (!firstInBlock)
                {
                    Inlines?.Add(new LineBreak());
                }
                AddRun(bulletMatch.Groups[1].Value + "• " + bulletMatch.Groups[2].Value);
                i++;
                firstInBlock = false;
                continue;
            }
            // 2026-08-06: numbered list
            // "1. text" / "  2. text" —
            // rendered with the user-typed
            // number kept verbatim. The
            // leading indent (if any) is
            // preserved like the bullet
            // path so a nested numbered
            // list gets the same visual
            // indent. The body text is
            // routed through the inline
            // parser so it picks up the
            // same code / bold / italic /
            // link handling as plain
            // paragraphs.
            var numberedMatch = NumberedListPattern.Match(line);
            if (numberedMatch.Success)
            {
                if (!firstInBlock)
                {
                    Inlines?.Add(new LineBreak());
                }
                AppendInlineMarkdown(
                    numberedMatch.Groups[1].Value
                    + numberedMatch.Groups[2].Value
                    + ". "
                    + numberedMatch.Groups[3].Value);
                i++;
                firstInBlock = false;
                continue;
            }
            // Plain text paragraph line.
            // Route through the inline
            // parser (still handles inline
            // code + bold) so this control
            // is a single-pass parser.
            if (!firstInBlock)
            {
                Inlines?.Add(new LineBreak());
            }
            AppendInlineMarkdown(line);
            i++;
            firstInBlock = false;
        }
    }

    // Render a fenced code block as a single
    // monospace Run with the code-bg
    // background. The block preserves internal
    // newlines via the LineBreak inserted at
    // the end of each line.
    private void AddCodeBlock(string[] lines, int startInclusive, int endExclusive)
    {
        for (var j = startInclusive; j < endExclusive; j++)
        {
            if (j > startInclusive)
            {
                Inlines?.Add(new LineBreak());
            }
            Inlines?.Add(new Run(lines[j])
            {
                FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, monospace"),
                Background = InlineCodeBackground.Value
            });
        }
    }

    // Render an ATX header. The level
    // controls the font size — the
    // hierarchy matches typical markdown
    // rendering (1=20, 2=17, 3=15). All
    // headers are SemiBold so they stand
    // out from body text without screaming.
    private void AddHeader(int level, string text)
    {
        var size = level switch
        {
            1 => 20.0,
            2 => 17.0,
            _ => 15.0
        };
        var run = new Run(text)
        {
            FontWeight = FontWeight.SemiBold,
            FontSize = size
        };
        Inlines?.Add(run);
    }

    // Render a blockquote. The visual is
    // "▎ " (U+258A) followed by the text
    // in italic + accent brush. The
    // blockquote character is a small
    // left-bar that visually approximates
    // a real blockquote's left border
    // without needing a per-Run Border
    // wrapper.
    private void AddBlockquote(string text)
    {
        Inlines?.Add(new Run("▎ ")
        {
            Foreground = BlockquoteBrush.Value,
            FontWeight = FontWeight.SemiBold
        });
        AppendInlineMarkdown(text, italic: true, foreground: BlockquoteBrush.Value);
    }

    private void AppendInlineMarkdown(string text, bool italic = false, IBrush? foreground = null, InlineCollection? target = null)
    {
        // 2026-08-06: replaced the
        // hand-rolled state machine with a
        // single regex that tries the
        // alternatives in priority order.
        // The previous shape only knew
        // about `code` and **bold**;
        // adding inline italic (*text* /
        // _text_) and links ([text](url))
        // would have required yet more
        // hand-rolled branches and the
        // priority logic between "is this
        // ** a bold marker or two
        // single-* italic markers" gets
        // fragile fast. The regex is
        // leftmost-first per alternation —
        // the link / code / bold
        // alternatives win over the italic
        // alternatives at the same
        // position, and the single-*
        // italic lookarounds keep it from
        // eating one half of a ** pair.
        //
        // `target` lets AddLink nest a
        // recursive call into a Bold's
        // InlineCollection (the link text
        // needs to be re-parsed through
        // the same inline path so e.g.
        // [**bold link**](url) gets the
        // inner bold applied on top of
        // the link's own bold wrapper).
        // TextBlock.Inlines is a
        // get-only collection so we
        // can't redirect `this.Inlines`
        // — instead the parser writes to
        // `target ?? this.Inlines`.
        var sink = target ?? Inlines;
        var index = 0;
        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > index)
            {
                AddRun(text[index..match.Index], italic, foreground, sink);
            }

            if (match.Groups["link"].Success)
            {
                AddLinkInto(
                    sink,
                    match.Groups["linkText"].Value,
                    match.Groups["linkUrl"].Value,
                    italic,
                    foreground);
            }
            else if (match.Groups["code"].Success)
            {
                sink?.Add(new Run(match.Groups["code"].Value)
                {
                    FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, monospace"),
                    Background = InlineCodeBackground.Value
                });
            }
            else if (match.Groups["bold"].Success)
            {
                var bold = new Bold();
                var inner = new Run(match.Groups["bold"].Value);
                if (italic)
                {
                    inner.FontStyle = FontStyle.Italic;
                }
                bold.Inlines?.Add(inner);
                sink?.Add(bold);
            }
            else if (match.Groups["italicA"].Success)
            {
                sink?.Add(new Run(match.Groups["italicA"].Value)
                {
                    FontStyle = FontStyle.Italic
                });
            }
            else if (match.Groups["italicU"].Success)
            {
                sink?.Add(new Run(match.Groups["italicU"].Value)
                {
                    FontStyle = FontStyle.Italic
                });
            }

            index = match.Index + match.Length;
        }

        if (index < text.Length)
        {
            AddRun(text[index..], italic, foreground, sink);
        }
    }

    // 2026-08-06: render a markdown link
    // [text](url) into an explicit
    // InlineCollection. The link text
    // is bolded (the user gets a clear
    // visual "this is a link" cue) and
    // the URL is appended in a dimmed
    // Run with the same parent
    // foreground so the user can read
    // the actual target. There is no
    // click handler — the parent is a
    // TextBlock, which has no built-in
    // way to route a click back to the
    // view-model, and a future iteration
    // that swaps the parent control
    // would have to re-wire the link
    // affordance anyway. The user can
    // still copy the URL via the
    // existing 复制 button on the AI
    // bubble (the full Detail string
    // ends up on the clipboard) or by
    // mouse-drag selecting the dimmed
    // URL run.
    //
    // The link text is routed through
    // AppendInlineMarkdown so an agent
    // that writes [**link
    // text**](url) gets the bold
    // applied to "link text" (inside
    // the link's own bold wrapper)
    // rather than the literal `**`
    // characters leaking into the
    // rendered text. The link's own
    // bold wrapper around the inner
    // inline tree is what makes the
    // "this is a link" visual cue
    // survive — the inner bold just
    // becomes an additional weight
    // delta on top.
    //
    // `target` parameter is explicit
    // (not "this.Inlines") so the
    // outer call from AppendInlineMarkdown
    // can route the link into a Bold
    // wrapper's children without
    // touching the outer TextBlock's
    // collection.
    private void AddLinkInto(InlineCollection? target, string linkText, string linkUrl, bool italic, IBrush? foreground)
    {
        var dim = foreground ?? DimTextBrush.Value;
        var bold = new Bold();
        // Recurse into the bold's Inlines so the linkText goes
        // through the same inline parser (handles `code` /
        // **bold** / *italic* / nested [link] inside the link
        // text). Without this recursion [**link**](url) would
        // render the literal `**` characters.
        AppendInlineMarkdown(linkText, italic, foreground, bold.Inlines);
        target?.Add(bold);

        // Parens around the URL are
        // rendered as dim text so the
        // URL visually breaks away from
        // the link text. A small space
        // keeps the "(url)" from
        // colliding with the link text
        // (which already has a font
        // weight change as its
        // separator).
        target?.Add(new Run(" (") { Foreground = dim });
        target?.Add(new Run(linkUrl) { Foreground = dim });
        target?.Add(new Run(")") { Foreground = dim });
    }

    // Dim text brush for the link
    // "(url)" tail. Resolved lazily
    // from the design-token dictionary
    // (MutedBrush) so the same hex
    // never escapes the Tokens files.
    // Falls back to a Gray brush if
    // the resource isn't present (e.g.
    // design-time / unit-test hosts).
    private static readonly Lazy<IBrush> DimTextBrush = new(() =>
    {
        if (AvaloniaApplication.Current?.Resources.TryGetResource("MutedBrush", null, out var resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }
        return Brushes.Gray;
    });

    private void AddRun(string text, bool italic = false, IBrush? foreground = null, InlineCollection? target = null)
    {
        if (text.Length == 0)
        {
            return;
        }
        var run = new Run(text);
        if (italic)
        {
            run.FontStyle = FontStyle.Italic;
        }
        if (foreground is not null)
        {
            run.Foreground = foreground;
        }
        (target ?? Inlines)?.Add(run);
    }
}
