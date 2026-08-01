using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AIChat.Domain.Artifacts;

namespace AIChat.Application.Artifacts;

// Kind inference + text extraction — split out of
// InputArtifactService so the service file only carries CRUD
// + reference rendering. DetermineKind is a static lookup
// over mime type / extension, ExtractText dispatches to the
// per-binary-format extractors (docx / xlsx / pdf / raw text).
// The binary-format extractors used to live alongside the
// service; pulling them out here keeps the 150 lines of
// zip-archive + PDF-literal parsing in a single file that
// the next maintainer can scan in one pass.
public static class InputArtifactClassifier
{
    public static InputArtifactKind DetermineKind(string fileName, string mimeType)
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

    public static string ExtractText(InputArtifactCreateRequest request, InputArtifactKind kind)
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
