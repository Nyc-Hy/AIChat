using AIChat.Domain.Chat;
using AIChat.Application.Artifacts;

namespace AIChat.Application.Context;

public sealed class ContextRouter
{
    private readonly FileRelevanceScorer _fileScorer;
    private readonly InputArtifactService _inputArtifactService;

    public ContextRouter(FileRelevanceScorer? fileScorer = null, InputArtifactService? inputArtifactService = null)
    {
        _fileScorer = fileScorer ?? new FileRelevanceScorer();
        _inputArtifactService = inputArtifactService ?? new InputArtifactService();
    }

    public TaskContextPack Route(ContextRouterRequest request)
    {
        var candidates = (request.FileIndex?.Entries ?? [])
            .Select(entry => _fileScorer.Score(
                entry,
                request.Goal,
                request.Phase,
                request.PinnedItems,
                request.ConversationMessages,
                request.WorkspaceChanges))
            .Where(score => score.Score > 0)
            .OrderByDescending(score => score.Score)
            .ThenBy(score => score.SizeBytes)
            .ToList();

        var included = new List<TaskContextFileRef>();
        var omitted = new List<TaskContextFileRef>();
        var estimatedTokens = 0;
        var budget = Math.Max(200, request.MaxTokens);

        foreach (var candidate in candidates)
        {
            var fileRef = new TaskContextFileRef
            {
                Path = candidate.Path,
                TypeTag = candidate.TypeTag,
                SizeBytes = candidate.SizeBytes,
                Score = candidate.Score,
                Reason = candidate.Reason
            };

            var fileTokens = EstimateFileRefTokens(fileRef);
            if (candidate.SizeBytes > request.MaxFileSizeBytes ||
                estimatedTokens + fileTokens > budget)
            {
                omitted.Add(fileRef);
                continue;
            }

            included.Add(fileRef);
            estimatedTokens += fileTokens;
        }

        var snippets = BuildPinnedSnippets(request, budget - estimatedTokens);
        snippets.AddRange(BuildMemorySnippets(request, budget - estimatedTokens - snippets.Sum(EstimateTokens)));
        estimatedTokens += snippets.Sum(EstimateTokens);

        var artifactRefs = request.Artifacts
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.Summary) || !string.IsNullOrWhiteSpace(artifact.ToolName))
            .Take(5)
            .Select(artifact => $"{artifact.ToolName}:{artifact.Kind}:{Truncate(artifact.Summary, 160)}")
            .ToList();

        artifactRefs.AddRange(_inputArtifactService.BuildPromptRefs(request.InputArtifacts, 8));
        estimatedTokens += artifactRefs.Sum(EstimateTokens);

        return new TaskContextPack
        {
            Summary = BuildSummary(included, omitted, snippets, artifactRefs, estimatedTokens),
            IncludedFiles = included,
            IncludedSnippets = snippets,
            ArtifactRefs = artifactRefs,
            OmittedButRelevantRefs = omitted.Take(12).ToList(),
            EstimatedTokens = estimatedTokens
        };
    }

    private static List<string> BuildPinnedSnippets(ContextRouterRequest request, int remainingTokens)
    {
        if (remainingTokens <= 0)
        {
            return [];
        }

        var snippets = new List<string>();
        var usedTokens = 0;
        foreach (var item in request.PinnedItems.Take(8))
        {
            var label = string.IsNullOrWhiteSpace(item.Note) ? item.Path : $"{item.Path} - {item.Note}";
            if (item.StartLine > 0)
            {
                label += item.EndLine > item.StartLine
                    ? $" (lines {item.StartLine}-{item.EndLine})"
                    : $" (line {item.StartLine})";
            }

            var tokens = EstimateTokens(label);
            if (usedTokens + tokens > remainingTokens)
            {
                break;
            }

            snippets.Add(label);
            usedTokens += tokens;
        }

        return snippets;
    }

    private static List<string> BuildMemorySnippets(ContextRouterRequest request, int remainingTokens)
    {
        if (remainingTokens <= 0)
        {
            return [];
        }

        var snippets = new List<string>();
        var usedTokens = 0;
        foreach (var memory in request.MemorySnippets.Take(8))
        {
            var label = "memory: " + Truncate(memory, 300);
            var tokens = EstimateTokens(label);
            if (usedTokens + tokens > remainingTokens)
            {
                break;
            }

            snippets.Add(label);
            usedTokens += tokens;
        }

        return snippets;
    }

    private static string BuildSummary(
        IReadOnlyList<TaskContextFileRef> included,
        IReadOnlyList<TaskContextFileRef> omitted,
        IReadOnlyList<string> snippets,
        IReadOnlyList<string> artifacts,
        int estimatedTokens)
    {
        return $"Context pack: {included.Count} files, {snippets.Count} snippets, {artifacts.Count} artifacts, {omitted.Count} omitted, ~{estimatedTokens} tokens";
    }

    private static int EstimateFileRefTokens(TaskContextFileRef fileRef)
    {
        var sizeHint = fileRef.SizeBytes <= 0 ? 20 : Math.Min(200, (int)Math.Ceiling(fileRef.SizeBytes / 1024.0) * 8);
        return EstimateTokens(fileRef.Path + fileRef.Reason) + sizeHint;
    }

    private static int EstimateTokens(string text)
    {
        return Math.Max(1, (int)Math.Ceiling((text?.Length ?? 0) / 4.0));
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }
}
