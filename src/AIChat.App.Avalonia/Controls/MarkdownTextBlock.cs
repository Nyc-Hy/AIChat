using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace AIChat.App.Avalonia.Controls;

public sealed class MarkdownTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, string?>(nameof(Markdown));

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

        var normalized = NormalizeMarkdown(markdown);
        var lines = normalized.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            AppendInlineMarkdown(lines[i]);
            if (i < lines.Length - 1)
            {
                Inlines?.Add(new LineBreak());
            }
        }
    }

    private static string NormalizeMarkdown(string markdown)
    {
        return Regex.Replace(markdown, @"(?m)^\s*[-*]\s+", "• ");
    }

    private void AppendInlineMarkdown(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var nextCode = text.IndexOf('`', index);
            var nextBold = text.IndexOf("**", index, StringComparison.Ordinal);
            var next = MinPositive(nextCode, nextBold);
            if (next < 0)
            {
                AddRun(text[index..]);
                return;
            }

            if (next > index)
            {
                AddRun(text[index..next]);
            }

            if (next == nextCode)
            {
                var end = text.IndexOf('`', next + 1);
                if (end < 0)
                {
                    AddRun(text[next..]);
                    return;
                }

                Inlines?.Add(new Run(text[(next + 1)..end])
                {
                    FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, monospace"),
                    Background = Brush.Parse("#E8EDF5")
                });
                index = end + 1;
            }
            else
            {
                var end = text.IndexOf("**", next + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    AddRun(text[next..]);
                    return;
                }

                var bold = new Bold();
                bold.Inlines?.Add(new Run(text[(next + 2)..end]));
                Inlines?.Add(bold);
                index = end + 2;
            }
        }
    }

    private void AddRun(string text)
    {
        if (text.Length > 0)
        {
            Inlines?.Add(new Run(text));
        }
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
