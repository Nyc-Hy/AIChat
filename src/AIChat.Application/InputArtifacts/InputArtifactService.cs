using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.IO.Compression;
using AIChat.Domain.Artifacts;

namespace AIChat.Application.Artifacts;

public sealed class InputArtifactService
{
    private const int SummaryMaxChars = 1200;

    public InputArtifact Create(InputArtifactCreateRequest request)
    {
        var kind = DetermineKind(request.FileName, request.MimeType);
        var rawText = Normalize(ExtractText(request, kind));
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
        artifact.Metadata["extraction"] = string.IsNullOrWhiteSpace(rawText) ? "metadata" : "text";

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

    public int Prune(ICollection<InputArtifact> artifacts, InputArtifactCleanupOptions? options = null)
    {
        return PruneRemoved(artifacts, options).Count;
    }

    public IReadOnlyList<InputArtifact> PruneRemoved(ICollection<InputArtifact> artifacts, InputArtifactCleanupOptions? options = null)
    {
        options ??= new InputArtifactCleanupOptions();
        var keepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in artifacts
                     .Where(artifact => !string.IsNullOrWhiteSpace(artifact.ConversationId))
                     .GroupBy(artifact => artifact.ConversationId, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var artifact in group
                         .OrderByDescending(artifact => artifact.CreatedAt)
                         .Take(Math.Max(0, options.MaxArtifactsPerConversation)))
            {
                keepIds.Add(artifact.Id);
            }
        }

        foreach (var artifact in artifacts
                     .Where(artifact => string.IsNullOrWhiteSpace(artifact.ConversationId))
                     .OrderByDescending(artifact => artifact.CreatedAt)
                     .Take(Math.Max(0, options.MaxProjectLevelArtifacts)))
        {
            keepIds.Add(artifact.Id);
        }

        var removed = artifacts
            .Where(artifact => !keepIds.Contains(artifact.Id))
            .ToList();
        foreach (var artifact in removed)
        {
            artifacts.Remove(artifact);
        }

        return removed;
    }

    public IReadOnlyList<InputArtifact> RemoveForConversation(ICollection<InputArtifact> artifacts, string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return [];
        }

        var removed = artifacts
            .Where(artifact => string.Equals(artifact.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var artifact in removed)
        {
            artifacts.Remove(artifact);
        }

        return removed;
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

    private static string ExtractText(InputArtifactCreateRequest request, InputArtifactKind kind)
    {
        if (!string.IsNullOrWhiteSpace(request.ContentText))
        {
            return request.ContentText;
        }

        if (request.FileBytes.Length == 0)
        {
            return "";
        }

        var extension = Path.GetExtension(request.FileName).TrimStart('.').ToLowerInvariant();
        try
        {
            return extension switch
            {
                "docx" => ExtractDocxText(request.FileBytes),
                "xlsx" => ExtractXlsxText(request.FileBytes),
                "pdf" => ExtractPdfText(request.FileBytes),
                "csv" or "tsv" => DecodeText(request.FileBytes),
                _ when kind == InputArtifactKind.Text => DecodeText(request.FileBytes),
                _ => ""
            };
        }
        catch
        {
            return "";
        }
    }

    private static string ExtractDocxText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml");
        if (entry is null)
        {
            return "";
        }

        using var entryStream = entry.Open();
        var document = XDocument.Load(entryStream);
        var builder = new StringBuilder();
        foreach (var paragraph in document.Descendants().Where(element => element.Name.LocalName == "p"))
        {
            var text = string.Concat(paragraph
                .Descendants()
                .Where(element => element.Name.LocalName == "t")
                .Select(element => element.Value));
            if (!string.IsNullOrWhiteSpace(text))
            {
                builder.AppendLine(text.Trim());
            }
        }

        return builder.ToString().Trim();
    }

    private static string ExtractXlsxText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var sharedStrings = ReadSharedStrings(archive);
        var builder = new StringBuilder();
        foreach (var entry in archive.Entries
                     .Where(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase))
        {
            using var entryStream = entry.Open();
            var document = XDocument.Load(entryStream);
            foreach (var row in document.Descendants().Where(element => element.Name.LocalName == "row"))
            {
                var cells = row
                    .Descendants()
                    .Where(element => element.Name.LocalName == "c")
                    .Select(cell => ReadCellValue(cell, sharedStrings))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
                if (cells.Count > 0)
                {
                    builder.AppendLine(string.Join('\t', cells));
                }
            }
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "si")
            .Select(item => string.Concat(item
                .Descendants()
                .Where(element => element.Name.LocalName == "t")
                .Select(element => element.Value)))
            .ToList();
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = cell.Attribute("t")?.Value ?? "";
        var value = cell.Descendants().FirstOrDefault(element => element.Name.LocalName == "v")?.Value ?? "";
        if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value, out var index) &&
            index >= 0 &&
            index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell
                .Descendants()
                .Where(element => element.Name.LocalName == "t")
                .Select(element => element.Value));
        }

        return value;
    }

    private static string ExtractPdfText(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var matches = Regex.Matches(text, @"\((?:\\.|[^\\)])*\)\s*Tj|\[(?<array>.*?)\]\s*TJ", RegexOptions.Singleline);
        var builder = new StringBuilder();
        foreach (Match match in matches)
        {
            if (match.Value.EndsWith("Tj", StringComparison.Ordinal))
            {
                var literal = match.Value[..^2].Trim();
                builder.AppendLine(UnescapePdfLiteral(literal));
                continue;
            }

            var array = match.Groups["array"].Value;
            foreach (Match literalMatch in Regex.Matches(array, @"\((?:\\.|[^\\)])*\)", RegexOptions.Singleline))
            {
                builder.Append(UnescapePdfLiteral(literalMatch.Value));
            }

            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string UnescapePdfLiteral(string literal)
    {
        var trimmed = literal.Trim();
        if (trimmed.StartsWith('(') && trimmed.EndsWith(')'))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\(", "(", StringComparison.Ordinal)
            .Replace("\\)", ")", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string DecodeText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
