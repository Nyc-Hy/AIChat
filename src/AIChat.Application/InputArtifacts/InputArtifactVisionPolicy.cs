using AIChat.Domain.Artifacts;

namespace AIChat.Application.Artifacts;

public enum InputArtifactVisionStatus
{
    NotImage,
    ModelDoesNotSupportVision,
    MissingStoredFile,
    UnsupportedImageType,
    EmptyFile,
    FileTooLarge,
    TooManyImages,
    TotalSizeTooLarge,
    Sendable
}

public sealed record InputArtifactVisionDecision(
    InputArtifact Artifact,
    InputArtifactVisionStatus Status,
    string StatusText,
    string MediaType,
    string StoredPath,
    long SizeBytes)
{
    public bool IsImage => Status != InputArtifactVisionStatus.NotImage;
    public bool CanSend => Status == InputArtifactVisionStatus.Sendable;
}

public static class InputArtifactVisionPolicy
{
    public const int MaxImagesPerRequest = 3;
    public const long MaxImageBytes = 4 * 1024 * 1024;
    public const long MaxTotalImageBytes = 8 * 1024 * 1024;

    public static IReadOnlyList<InputArtifactVisionDecision> Evaluate(
        IEnumerable<InputArtifact> artifacts,
        bool modelSupportsVision)
    {
        var ordered = artifacts
            .Where(artifact => artifact is not null)
            .OrderByDescending(artifact => artifact.CreatedAt)
            .ToList();
        var decisions = new List<InputArtifactVisionDecision>();
        var sendableCount = 0;
        long totalBytes = 0;

        foreach (var artifact in ordered)
        {
            var mediaType = ResolveImageMediaType(artifact);
            var storedPath = artifact.Metadata.TryGetValue("storedPath", out var value) ? value : "";
            var sizeBytes = TryGetSize(storedPath);

            if (!IsImageArtifact(artifact))
            {
                decisions.Add(Decision(artifact, InputArtifactVisionStatus.NotImage, mediaType, storedPath, sizeBytes));
                continue;
            }

            if (!modelSupportsVision)
            {
                decisions.Add(Decision(artifact, InputArtifactVisionStatus.ModelDoesNotSupportVision, mediaType, storedPath, sizeBytes));
                continue;
            }

            if (string.IsNullOrWhiteSpace(storedPath) || !File.Exists(storedPath))
            {
                decisions.Add(Decision(artifact, InputArtifactVisionStatus.MissingStoredFile, mediaType, storedPath, sizeBytes));
                continue;
            }

            if (string.IsNullOrWhiteSpace(mediaType))
            {
                decisions.Add(Decision(artifact, InputArtifactVisionStatus.UnsupportedImageType, mediaType, storedPath, sizeBytes));
                continue;
            }

            if (sizeBytes <= 0)
            {
                decisions.Add(Decision(artifact, InputArtifactVisionStatus.EmptyFile, mediaType, storedPath, sizeBytes));
                continue;
            }

            if (sizeBytes > MaxImageBytes)
            {
                decisions.Add(Decision(artifact, InputArtifactVisionStatus.FileTooLarge, mediaType, storedPath, sizeBytes));
                continue;
            }

            if (sendableCount >= MaxImagesPerRequest)
            {
                decisions.Add(Decision(artifact, InputArtifactVisionStatus.TooManyImages, mediaType, storedPath, sizeBytes));
                continue;
            }

            if (totalBytes + sizeBytes > MaxTotalImageBytes)
            {
                decisions.Add(Decision(artifact, InputArtifactVisionStatus.TotalSizeTooLarge, mediaType, storedPath, sizeBytes));
                continue;
            }

            sendableCount++;
            totalBytes += sizeBytes;
            decisions.Add(Decision(artifact, InputArtifactVisionStatus.Sendable, mediaType, storedPath, sizeBytes));
        }

        return decisions;
    }

    public static InputArtifactVisionDecision EvaluateSingle(InputArtifact artifact, bool modelSupportsVision)
    {
        return Evaluate([artifact], modelSupportsVision).First();
    }

    private static InputArtifactVisionDecision Decision(
        InputArtifact artifact,
        InputArtifactVisionStatus status,
        string mediaType,
        string storedPath,
        long sizeBytes)
    {
        return new InputArtifactVisionDecision(
            artifact,
            status,
            StatusText(status),
            mediaType,
            storedPath,
            sizeBytes);
    }

    private static string StatusText(InputArtifactVisionStatus status)
    {
        return status switch
        {
            InputArtifactVisionStatus.NotImage => "",
            InputArtifactVisionStatus.ModelDoesNotSupportVision => "当前模型不支持图片输入，仅作为附件引用",
            InputArtifactVisionStatus.MissingStoredFile => "托管文件不存在，仅作为附件引用",
            InputArtifactVisionStatus.UnsupportedImageType => "图片类型不支持，仅作为附件引用",
            InputArtifactVisionStatus.EmptyFile => "图片为空，仅作为附件引用",
            InputArtifactVisionStatus.FileTooLarge => "图片过大，仅作为附件引用",
            InputArtifactVisionStatus.TooManyImages => "本轮图片数量已达上限，仅作为附件引用",
            InputArtifactVisionStatus.TotalSizeTooLarge => "本轮图片总大小已达上限，仅作为附件引用",
            InputArtifactVisionStatus.Sendable => "将发送给模型",
            _ => ""
        };
    }

    private static bool IsImageArtifact(InputArtifact artifact)
    {
        return artifact.Kind is InputArtifactKind.Image or InputArtifactKind.Screenshot ||
               artifact.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveImageMediaType(InputArtifact artifact)
    {
        if (artifact.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return artifact.MimeType;
        }

        return Path.GetExtension(artifact.FileName).TrimStart('.').ToLowerInvariant() switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            _ => ""
        };
    }

    private static long TryGetSize(string storedPath)
    {
        try
        {
            return string.IsNullOrWhiteSpace(storedPath) || !File.Exists(storedPath)
                ? 0
                : new FileInfo(storedPath).Length;
        }
        catch
        {
            return 0;
        }
    }
}
