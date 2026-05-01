namespace AIChat.Application.Verification;

public static class VerificationResultParser
{
    private static readonly string[] ErrorPatterns =
    [
        ": error ",
        "Error(s)",
        "FAILED",
        "失败",
        "Build FAILED",
        "error CS",
        "error TS",
        "fatal error"
    ];

    private static readonly string[] WarningPatterns =
    [
        ": warning "
    ];

    public static string Summarize(string output, int maxLines = 20)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "";
        }

        var lines = output.Split('\n', StringSplitOptions.None);
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (ErrorPatterns.Any(p => trimmed.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(trimmed);
            }
            else if (WarningPatterns.Any(p => trimmed.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(trimmed);
            }
        }

        var result = new List<string>();

        if (errors.Count > 0)
        {
            result.AddRange(errors.Take(maxLines));
            var remaining = maxLines - result.Count;
            if (remaining > 0)
            {
                result.AddRange(warnings.Take(remaining));
            }
        }
        else if (warnings.Count > 0)
        {
            result.AddRange(warnings.Take(maxLines));
        }
        else
        {
            // No errors or warnings — take the tail
            var nonEmpty = lines
                .Where(l => !string.IsNullOrWhiteSpace(l.TrimEnd('\r')))
                .Select(l => l.TrimEnd('\r'))
                .ToList();
            result.AddRange(nonEmpty.Skip(Math.Max(0, nonEmpty.Count - maxLines)));
        }

        if (result.Count == 0)
        {
            return "";
        }

        var summary = string.Join('\n', result);
        if (lines.Length > maxLines && result.Count >= maxLines)
        {
            summary += "\n...";
        }

        return summary;
    }
}
