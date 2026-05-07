using System.Text;
using AIChat.Domain.Artifacts;

namespace AIChat.Application.Artifacts;

public sealed class InputArtifactService
{
    private const int SummaryMaxChars = 1200;

    public InputArtifact Create(InputArtifactCreateRequest request)
    {
        var kind = DetermineKind(request.FileName, request.MimeType);
        var rawText = Normalize(request.ContentText);
        var artifact = new InputArtifact
        {
            ProjectId = request.ProjectId,
            ConversationId = request.ConversationId,
            MessageId = request.MessageId,
            FileName = request.FileName.Trim(),
            MimeType = request.MimeType.Trim(),
            Kind = kind,
            RawText = rawText,
            Summary = BuildSummary(kind, request.FileName, request.MimeType, rawText),
            CreatedAt = DateTimeOffset.Now,
            Metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        };

        if (!artifact.Metadata.ContainsKey("ref"))
        {
            artifact.Metadata["ref"] = artifact.RefId;
        }

        artifact.Metadata["kind"] = kind.ToString();
        artifact.Metadata["fileName"] = artifact.FileName;
        artifact.Metadata["mimeType"] = artifact.MimeType;
        artifact.Metadata["charCount"] = rawText.Length.ToString();

        if (!artifact.Metadata.ContainsKey("extension"))
        {
            artifact.Metadata["extension"] = Path.GetExtension(artifact.FileName).TrimStart('.');
        }

        return artifact;
    }

    public string ToPromptRef(InputArtifact artifact)
    {
        var fileName = string.IsNullOrWhiteSpace(artifact.FileName) ? "(unnamed)" : artifact.FileName.Trim();
        var summary = string.IsNullOrWhiteSpace(artifact.Summary)
            ? "metadata only; request details by artifact ref if needed"
            : Truncate(artifact.Summary, 220);
        return $"{artifact.RefId} [{artifact.Kind}] {fileName}: {summary}";
    }

    public IReadOnlyList<string> BuildPromptRefs(IEnumerable<InputArtifact> artifacts, int maxCount = 8)
    {
        return artifacts
            .Where(artifact => artifact is not null)
            .OrderByDescending(artifact => artifact.CreatedAt)
            .Take(Math.Max(0, maxCount))
            .Select(ToPromptRef)
            .ToList();
    }

    public string GetDetail(InputArtifact artifact, int maxChars = 4000)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{artifact.RefId} [{artifact.Kind}] {artifact.FileName}".Trim());
        if (!string.IsNullOrWhiteSpace(artifact.MimeType))
        {
            builder.AppendLine($"Mime type: {artifact.MimeType}");
        }

        if (!string.IsNullOrWhiteSpace(artifact.Summary))
        {
            builder.AppendLine("Summary:");
            builder.AppendLine(artifact.Summary.Trim());
        }

        if (!string.IsNullOrWhiteSpace(artifact.Description))
        {
            builder.AppendLine("Description:");
            builder.AppendLine(Truncate(artifact.Description, maxChars));
        }

        if (!string.IsNullOrWhiteSpace(artifact.OcrText))
        {
            builder.AppendLine("OCR:");
            builder.AppendLine(Truncate(artifact.OcrText, maxChars));
        }

        if (!string.IsNullOrWhiteSpace(artifact.RawText))
        {
            builder.AppendLine("Raw text:");
            builder.AppendLine(Truncate(artifact.RawText, maxChars));
        }

        return builder.ToString().Trim();
    }

    private static InputArtifactKind DetermineKind(string fileName, string mimeType)
    {
        var mime = mimeType.Trim().ToLowerInvariant();
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (mime.Contains("screenshot", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("screenshot", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("screen-shot", StringComparison.OrdinalIgnoreCase))
        {
            return InputArtifactKind.Screenshot;
        }

        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            extension is "png" or "jpg" or "jpeg" or "gif" or "webp" or "bmp")
        {
            return InputArtifactKind.Image;
        }

        if (mime.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase) ||
            mime.Contains("excel", StringComparison.OrdinalIgnoreCase) ||
            mime.Contains("csv", StringComparison.OrdinalIgnoreCase) ||
            extension is "xlsx" or "xls" or "csv" or "tsv")
        {
            return InputArtifactKind.Spreadsheet;
        }

        if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            extension is "txt" or "md" or "json" or "xml" or "yaml" or "yml")
        {
            return InputArtifactKind.Text;
        }

        if (mime.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
            mime.Contains("document", StringComparison.OrdinalIgnoreCase) ||
            extension is "pdf" or "doc" or "docx" or "rtf")
        {
            return InputArtifactKind.Document;
        }

        return string.IsNullOrWhiteSpace(mime) && string.IsNullOrWhiteSpace(extension)
            ? InputArtifactKind.Unknown
            : InputArtifactKind.Document;
    }

    private static string BuildSummary(InputArtifactKind kind, string fileName, string mimeType, string rawText)
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

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
    }

    private static string Truncate(string value, int maxChars)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }
}
