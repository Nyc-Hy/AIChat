using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AIChat.App.Controls;

// Minimal markdown renderer for chat messages. It supports the subset needed by
// this MVP: headings, bullets, bold text, and fenced code blocks.
public sealed class MarkdownBlock : FlowDocumentScrollViewer
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarkdownBlock),
            new PropertyMetadata("", OnTextChanged));

    public MarkdownBlock()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        IsToolBarVisible = false;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Document = CreateDocument("");
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var control = (MarkdownBlock)dependencyObject;
        control.Document = CreateDocument(e.NewValue as string ?? "");
    }

    private static FlowDocument CreateDocument(string markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(42, 51, 64)),
            PagePadding = new Thickness(0),
            LineHeight = 23
        };

        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var inCode = false;
        var codeLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                // Toggle fenced code mode; collected lines become one styled block.
                if (inCode)
                {
                    AddCodeBlock(document, string.Join(Environment.NewLine, codeLines));
                    codeLines.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                }

                continue;
            }

            if (inCode)
            {
                codeLines.Add(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                document.Blocks.Add(new Paragraph { Margin = new Thickness(0, 4, 0, 4) });
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                AddParagraph(document, line[4..], 15, FontWeights.SemiBold, new Thickness(0, 8, 0, 4));
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                AddParagraph(document, line[3..], 17, FontWeights.SemiBold, new Thickness(0, 10, 0, 5));
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                AddParagraph(document, line[2..], 19, FontWeights.SemiBold, new Thickness(0, 12, 0, 6));
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                AddParagraph(document, $"• {line[2..]}", 14, FontWeights.Normal, new Thickness(0, 2, 0, 2));
                continue;
            }

            AddParagraph(document, line, 14, FontWeights.Normal, new Thickness(0, 2, 0, 2));
        }

        if (codeLines.Count > 0)
        {
            AddCodeBlock(document, string.Join(Environment.NewLine, codeLines));
        }

        return document;
    }

    private static void AddParagraph(FlowDocument document, string text, double fontSize, FontWeight fontWeight, Thickness margin)
    {
        var paragraph = new Paragraph { Margin = margin, FontSize = fontSize, FontWeight = fontWeight };
        AddInlineRuns(paragraph, text);
        document.Blocks.Add(paragraph);
    }

    private static void AddInlineRuns(Paragraph paragraph, string text)
    {
        // Parse **bold** markers in a small hand-rolled pass. A full markdown
        // library can replace this control later if needed.
        var index = 0;
        while (index < text.Length)
        {
            var start = text.IndexOf("**", index, StringComparison.Ordinal);
            if (start < 0)
            {
                paragraph.Inlines.Add(new Run(text[index..]));
                return;
            }

            if (start > index)
            {
                paragraph.Inlines.Add(new Run(text[index..start]));
            }

            var end = text.IndexOf("**", start + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                paragraph.Inlines.Add(new Run(text[start..]));
                return;
            }

            paragraph.Inlines.Add(new Bold(new Run(text[(start + 2)..end])));
            index = end + 2;
        }
    }

    private static void AddCodeBlock(FlowDocument document, string code)
    {
        var paragraph = new Paragraph(new Run(code))
        {
            Margin = new Thickness(0, 8, 0, 8),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 230, 238)),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            LineHeight = 20
        };
        document.Blocks.Add(paragraph);
    }
}
