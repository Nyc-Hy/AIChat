using AIChat.Domain.Artifacts;
using AIChat.Application.Artifacts;
using System.IO;

namespace AIChat.App.ViewModels;

public sealed class InputArtifactViewModel
{
    private readonly InputArtifact _artifact;
    private readonly InputArtifactVisionDecision _visionDecision;

    public InputArtifactViewModel(InputArtifact artifact, bool canSendImagesToModel = false)
        : this(artifact, InputArtifactVisionPolicy.EvaluateSingle(artifact, canSendImagesToModel))
    {
    }

    public InputArtifactViewModel(InputArtifact artifact, InputArtifactVisionDecision visionDecision)
    {
        _artifact = artifact;
        _visionDecision = visionDecision;
    }

    public InputArtifact Artifact => _artifact;
    public string Id => _artifact.Id;
    public string RefId => _artifact.RefId;
    public string FileName => string.IsNullOrWhiteSpace(_artifact.FileName) ? "(unnamed)" : _artifact.FileName;
    public string Kind => _artifact.Kind.ToString();
    public string Summary => string.IsNullOrWhiteSpace(_artifact.Summary) ? "metadata only" : _artifact.Summary;
    public string MimeType => _artifact.MimeType;
    public string StoredPath => _visionDecision.StoredPath;
    public bool HasStoredFile => !string.IsNullOrWhiteSpace(StoredPath) && File.Exists(StoredPath);
    public bool IsImagePreview => _visionDecision.IsImage && HasStoredFile;
    public bool WillSendToModel => _visionDecision.CanSend;
    public string ModelDeliveryStatus => _visionDecision.StatusText;
    public bool HasModelDeliveryStatus => !string.IsNullOrWhiteSpace(ModelDeliveryStatus);
    public bool WasOptimized => _artifact.Metadata.TryGetValue("optimized", out var value) &&
                                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    public string DeliveryBadgeText => WasOptimized && WillSendToModel
        ? "已优化 · 多模态"
        : WillSendToModel ? "多模态" : _visionDecision.IsImage ? "引用" : "";
    public bool HasDeliveryBadge => !string.IsNullOrWhiteSpace(DeliveryBadgeText);
    public string PreviewImagePath => IsImagePreview ? StoredPath : "";
    public string PreviewTitle => $"{FileName} · {SizeText}".Trim(' ', '·');
    public string PreviewMetadata => string.Join(" · ", new[]
    {
        MimeType,
        ImageDimensionsText,
        WasOptimized ? BuildOptimizationDetail() : "",
        ModelDeliveryStatus,
        RefId
    }.Where(part => !string.IsNullOrWhiteSpace(part)));
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

            if (HasStoredFile)
            {
                lines.Add($"Stored: {StoredPath}");
            }

            if (WasOptimized)
            {
                lines.Add(BuildOptimizationDetail());
            }

            if (!string.IsNullOrWhiteSpace(ModelDeliveryStatus))
            {
                lines.Add(ModelDeliveryStatus);
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
    public string ImageDimensionsText => _artifact.Metadata.TryGetValue("imageWidth", out var width) &&
                                         _artifact.Metadata.TryGetValue("imageHeight", out var height)
        ? $"{width}x{height}"
        : "";

    public string Subtitle
    {
        get
        {
            var parts = new[] { Kind, SizeText, ImageDimensionsText, ModelDeliveryStatus, RefId }
                .Where(part => !string.IsNullOrWhiteSpace(part));
            return string.Join(" · ", parts);
        }
    }

    private string BuildOptimizationDetail()
    {
        var originalName = _artifact.Metadata.TryGetValue("originalFileName", out var name) ? name : "";
        var originalSize = _artifact.Metadata.TryGetValue("originalSizeBytes", out var value) &&
                           long.TryParse(value, out var bytes)
            ? FormatBytes(bytes)
            : "";
        return string.IsNullOrWhiteSpace(originalName + originalSize)
            ? "Optimized for multimodal input"
            : $"Optimized from {originalName} {originalSize}".Trim();
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
