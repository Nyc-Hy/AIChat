using AIChat.Domain.Memory;

namespace AIChat.Application.Memory;

public sealed class MemoryRetriever
{
    public IReadOnlyList<MemoryRetrievalResult> Retrieve(
        IEnumerable<MemoryEntry> entries,
        MemoryRetrievalRequest request)
    {
        var terms = ExtractTerms(request.Query);
        return entries
            .Where(entry => string.IsNullOrWhiteSpace(request.ProjectId) ||
                            string.IsNullOrWhiteSpace(entry.ProjectId) ||
                            string.Equals(entry.ProjectId, request.ProjectId, StringComparison.OrdinalIgnoreCase))
            .Where(entry => request.Categories.Contains(entry.Category))
            .Select(entry => Score(entry, terms))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.Entry.UpdatedAt)
            .Take(Math.Max(1, request.MaxResults))
            .ToList();
    }

    public IReadOnlyList<string> RetrieveSnippets(
        IEnumerable<MemoryEntry> entries,
        MemoryRetrievalRequest request)
    {
        return Retrieve(entries, request)
            .Select(result => $"[{result.Entry.Category}] {result.Entry.Content} (source: {result.Entry.Source})")
            .ToList();
    }

    private static MemoryRetrievalResult Score(MemoryEntry entry, IReadOnlyList<string> terms)
    {
        var score = entry.Category switch
        {
            MemoryCategory.Project => 2.0,
            MemoryCategory.Task => 1.5,
            MemoryCategory.Tool => 1.0,
            _ => 0.75
        };
        var reasons = new List<string> { entry.Category.ToString().ToLowerInvariant() };
        var haystack = $"{entry.Content} {entry.Source} {string.Join(' ', entry.Metadata.Values)}".ToLowerInvariant();
        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
                reasons.Add($"match:{term}");
            }
        }

        if (terms.Count == 0)
        {
            score += 0.5;
        }

        return new MemoryRetrievalResult(entry, score, string.Join(", ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<string> ExtractTerms(string text)
    {
        return text
            .ToLowerInvariant()
            .Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '/', '\\', '-', '_', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
