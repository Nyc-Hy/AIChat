namespace AIChat.App.ViewModels;

public sealed class DiffLineViewModel
{
    public DiffLineViewModel(string text)
    {
        Text = text;
        Kind = text.StartsWith("+++", StringComparison.Ordinal) ||
               text.StartsWith("---", StringComparison.Ordinal) ||
               text.StartsWith("@@", StringComparison.Ordinal)
            ? "Header"
            : text.StartsWith("+", StringComparison.Ordinal)
                ? "Added"
                : text.StartsWith("-", StringComparison.Ordinal)
                    ? "Removed"
                    : "Context";
    }

    public string Text { get; }
    public string Kind { get; }

    public static IReadOnlyList<DiffLineViewModel> FromDiff(string diffText)
    {
        return string.IsNullOrWhiteSpace(diffText)
            ? []
            : diffText.ReplaceLineEndings("\n")
                .Split('\n')
                .Select(line => new DiffLineViewModel(line))
                .ToList();
    }
}
