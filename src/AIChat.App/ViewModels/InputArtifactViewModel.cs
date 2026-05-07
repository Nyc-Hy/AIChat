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
}
