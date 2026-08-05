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
//   - inline code + bold (unchanged)
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

    private void AppendInlineMarkdown(string text, bool italic = false, IBrush? foreground = null)
    {
        var index = 0;
        while (index < text.Length)
        {
            var nextCode = text.IndexOf('`', index);
            var nextBold = text.IndexOf("**", index, StringComparison.Ordinal);
            var next = MinPositive(nextCode, nextBold);
            if (next < 0)
            {
                AddRun(text[index..], italic, foreground);
                return;
            }

            if (next > index)
            {
                AddRun(text[index..next], italic, foreground);
            }

            if (next == nextCode)
            {
                var end = text.IndexOf('`', next + 1);
                if (end < 0)
                {
                    AddRun(text[next..], italic, foreground);
                    return;
                }

                Inlines?.Add(new Run(text[(next + 1)..end])
                {
                    FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, monospace"),
                    Background = InlineCodeBackground.Value
                });
                index = end + 1;
            }
            else
            {
                var end = text.IndexOf("**", next + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    AddRun(text[next..], italic, foreground);
                    return;
                }

                var bold = new Bold();
                if (italic)
                {
                    bold.Inlines?.Add(new Run(text[(next + 2)..end])
                    {
                        FontStyle = FontStyle.Italic
                    });
                }
                else
                {
                    bold.Inlines?.Add(new Run(text[(next + 2)..end]));
                }
                Inlines?.Add(bold);
                index = end + 2;
            }
        }
    }

    private void AddRun(string text, bool italic = false, IBrush? foreground = null)
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
        Inlines?.Add(run);
    }

    private static int MinPositive(int a, int b)
    {
        return (a, b) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => b,
            (_, < 0) => a,
            _ => Math.Min(a, b)
        };
    }
}
