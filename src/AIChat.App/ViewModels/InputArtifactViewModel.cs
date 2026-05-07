using AIChat.Domain.Artifacts;

namespace AIChat.App.ViewModels;

public sealed class InputArtifactViewModel
{
    private readonly InputArtifact _artifact;

    public InputArtifactViewModel(InputArtifact artifact)
    {
        _artifact = artifact;
    }

    public InputArtifact Artifact => _artifact;
    public string Id => _artifact.Id;
    public string RefId => _artifact.RefId;
    public string FileName => string.IsNullOrWhiteSpace(_artifact.FileName) ? "(unnamed)" : _artifact.FileName;
    public string Kind => _artifact.Kind.ToString();
    public string Summary => string.IsNullOrWhiteSpace(_artifact.Summary) ? "metadata only" : _artifact.Summary;
    public string MimeType => _artifact.MimeType;
    public string CreatedAtText => _artifact.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string DetailPreview
    {
        get
        {
            var lines = new List<string>
            {
                FileName,
                Subtitle
            };
            if (!string.IsNullOrWhiteSpace(MimeType))
            {
                lines.Add($"Mime: {MimeType}");
            }

            lines.Add($"Created: {CreatedAtText}");
            lines.Add("");
            lines.Add(Truncate(Summary, 700));
            return string.Join(Environment.NewLine, lines.Where(line => line is not null));
        }
    }
    public string SizeText => _artifact.Metadata.TryGetValue("sizeBytes", out var value) &&
                              long.TryParse(value, out var sizeBytes)
        ? FormatBytes(sizeBytes)
        : "";

    public string Subtitle
    {
        get
        {
            var parts = new[] { Kind, SizeText, RefId }
                .Where(part => !string.IsNullOrWhiteSpace(part));
            return string.Join(" · ", parts);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kb = bytes / 1024.0;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        return $"{kb / 1024.0:0.#} MB";
    }

    private static string Truncate(string value, int maxChars)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }
}
