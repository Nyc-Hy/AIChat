namespace AIChat.Application.Tools;

internal static class ToolDiff
{
    internal static string CreateUnifiedDiff(string path, string oldText, string newText, int maxLines = 180)
    {
        var oldLines = oldText.ReplaceLineEndings("\n").Split('\n');
        var newLines = newText.ReplaceLineEndings("\n").Split('\n');
        var lines = new List<string>
        {
            $"--- a/{path.Replace('\\', '/')}",
            $"+++ b/{path.Replace('\\', '/')}"
        };

        var max = Math.Max(oldLines.Length, newLines.Length);
        for (var index = 0; index < max && lines.Count < maxLines; index++)
        {
            var oldLine = index < oldLines.Length ? oldLines[index] : null;
            var newLine = index < newLines.Length ? newLines[index] : null;
            if (oldLine == newLine)
            {
                continue;
            }

            if (oldLine is not null)
            {
                lines.Add("-" + oldLine);
            }

            if (newLine is not null)
            {
                lines.Add("+" + newLine);
            }
        }

        if (lines.Count >= maxLines)
        {
            lines.Add("...diff truncated...");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
