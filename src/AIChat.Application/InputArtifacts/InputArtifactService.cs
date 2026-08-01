using System.Text;
using AIChat.Domain.Artifacts;

namespace AIChat.Application.Artifacts;

// Input artifact CRUD + reference rendering — the kind
// inference and summary text moved to InputArtifactClassifier
// and InputArtifactSummarizer. This file now only owns the
// service-level surface the rest of the app talks to: create
// an artifact from a user upload, render it as a prompt
// ref, build a list of refs for the system prompt, and
// prune / remove artifacts per conversation. The two helper
// collaborators (Classifier / Summarizer) are pure static
// classes, so no DI changes are needed — the service
// references them directly.
public sealed class InputArtifactService
{
    public InputArtifact Create(InputArtifactCreateRequest request)
    {
        var kind = InputArtifactClassifier.DetermineKind(request.FileName, request.MimeType);
        var rawText = Normalize(InputArtifactClassifier.ExtractText(request, kind));
        var artifact = new InputArtifact
        {
            ProjectId = request.ProjectId,
            ConversationId = request.ConversationId,
            MessageId = request.MessageId,
            FileName = request.FileName.Trim(),
            MimeType = request.MimeType.Trim(),
            Kind = kind,
            RawText = rawText,
            Summary = InputArtifactSummarizer.BuildSummary(kind, request.FileName, request.MimeType, rawText),
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
