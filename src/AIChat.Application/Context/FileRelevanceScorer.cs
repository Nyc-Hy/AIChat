using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Workspace;
using AIChat.Domain.Chat;
using AIChat.Domain.Context;

namespace AIChat.Application.Context;

public sealed class FileRelevanceScorer
{
    public FileRelevanceScore Score(
        ProjectFileIndexEntry entry,
        string goal,
        AgentRunPhase phase,
        IReadOnlyList<PinnedContextItem> pinnedItems,
        IReadOnlyList<ChatMessage> conversationMessages,
        IReadOnlyList<WorkspaceChange> workspaceChanges)
    {
        var score = 0.0;
        var reasons = new List<string>();
        var path = entry.RelativePath.Replace('\\', '/');
        var pathLower = path.ToLowerInvariant();
        var goalTerms = ExtractTerms(goal);
        var conversationTerms = ExtractTerms(string.Join(' ', conversationMessages.TakeLast(12).Select(message => message.Content)));

        foreach (var term in goalTerms)
        {
            if (pathLower.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
                reasons.Add($"goal:{term}");
            }
        }

        foreach (var term in conversationTerms.Take(20))
        {
            if (pathLower.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
                reasons.Add($"conversation:{term}");
            }
        }

        if (pinnedItems.Any(item => PathsMatch(path, item.Path)))
        {
            score += 10;
            reasons.Add("pinned");
        }

        if (workspaceChanges.Any(change => PathsMatch(path, change.Path)))
        {
            score += 7;
            reasons.Add("recent-edit");
        }

        score += TypeScore(entry.TypeTag, phase, reasons);
        score += TestSourcePairScore(path, goalTerms, phase, reasons);

        if (entry.SizeBytes > 256 * 1024)
        {
            score -= 6;
            reasons.Add("large-file");
        }

        return new FileRelevanceScore(
            entry.RelativePath,
            entry.TypeTag,
            entry.SizeBytes,
            Math.Max(0, score),
            reasons.Count == 0 ? "low relevance" : string.Join(", ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static double TypeScore(string typeTag, AgentRunPhase phase, List<string> reasons)
    {
        if (phase == AgentRunPhase.Verifying || phase == AgentRunPhase.Repairing)
        {
            if (string.Equals(typeTag, "test", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("phase:test");
                return 5;
            }
        }

        if (phase == AgentRunPhase.GatheringContext &&
            (string.Equals(typeTag, "doc", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(typeTag, "config", StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add($"phase:{typeTag}");
            return 2;
        }

        if (phase == AgentRunPhase.Executing &&
            string.Equals(typeTag, "source", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("phase:source");
            return 3;
        }

        return 0;
    }

    private static double TestSourcePairScore(string pathLower, IReadOnlyList<string> goalTerms, AgentRunPhase phase, List<string> reasons)
    {
        if (phase != AgentRunPhase.Verifying && phase != AgentRunPhase.Repairing)
        {
            return 0;
        }

        var fileName = Path.GetFileNameWithoutExtension(pathLower).ToLowerInvariant();
        var normalized = fileName
            .Replace("tests", "", StringComparison.OrdinalIgnoreCase)
            .Replace("test", "", StringComparison.OrdinalIgnoreCase)
            .Replace("spec", "", StringComparison.OrdinalIgnoreCase);
        if (goalTerms.Any(term => term.Length > 2 && normalized.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add("test-source-pair");
            return 4;
        }

        return 0;
    }

    private static IReadOnlyList<string> ExtractTerms(string text)
    {
        return text
            .ToLowerInvariant()
            .Split(Path.GetInvalidFileNameChars().Concat([' ', '\r', '\n', '\t', '.', ',', ';', ':', '/', '\\', '-', '_', '(', ')', '[', ']']).Distinct().ToArray(), StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool PathsMatch(string left, string right)
    {
        var normalizedLeft = left.Replace('\\', '/').TrimStart('/');
        var normalizedRight = right.Replace('\\', '/').TrimStart('/');
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase) ||
               normalizedLeft.EndsWith("/" + normalizedRight, StringComparison.OrdinalIgnoreCase) ||
               normalizedRight.EndsWith("/" + normalizedLeft, StringComparison.OrdinalIgnoreCase);
    }
}
