using AIChat.Domain.Artifacts;

namespace AIChat.Application.Artifacts;

// Summary text builder — split out of InputArtifactService so
// the service file only carries CRUD + reference rendering. The
// summary is the human-readable (and LLM-readable) one-liner
// that goes into the artifact's Summary field and gets quoted
// in the conversation prompt. Pulled out because the kind
// switch + placeholder copy is the part that needs to grow
// when new artifact kinds land, and isolating it lets the
// diff stay small.
public static class InputArtifactSummarizer
{
    private const int SummaryMaxChars = 1200;

    public static string BuildSummary(
        InputArtifactKind kind,
        string fileName,
        string mimeType,
        string rawText)
    {
        var label = string.IsNullOrWhiteSpace(fileName) ? "attached input" : fileName.Trim();
        if (!string.IsNullOrWhiteSpace(rawText))
        {
            return $"{kind} {label}: {Truncate(rawText.ReplaceLineEndings(" "), SummaryMaxChars)}";
        }

        var typeHint = string.IsNullOrWhiteSpace(mimeType) ? "unknown mime type" : mimeType.Trim();
        return kind switch
        {
            InputArtifactKind.Image => $"Image {label} ({typeHint}); no OCR or image description has been extracted yet.",
            InputArtifactKind.Screenshot => $"Screenshot {label} ({typeHint}); no UI element summary has been extracted yet.",
            InputArtifactKind.Spreadsheet => $"Spreadsheet {label} ({typeHint}); no sheet summary has been extracted yet.",
            InputArtifactKind.Document => $"Document {label} ({typeHint}); no document text has been extracted yet.",
            _ => $"Input artifact {label} ({typeHint}); metadata only."
        };
    }

    private static string Truncate(string value, int maxChars)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }
}
