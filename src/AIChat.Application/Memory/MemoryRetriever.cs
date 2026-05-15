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
        var matchedTerms = 0;
        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
                matchedTerms++;
                reasons.Add($"match:{term}");
            }
        }

        if (IsVerificationFailureMemory(entry) && LooksLikeVerificationOrRepairQuery(terms))
        {
            score += 4;
            reasons.Add("verification-failure");
        }

        if (matchedTerms >= 2)
        {
            score += Math.Min(4, matchedTerms - 1);
        }

        if (terms.Count == 0)
        {
            score += 0.5;
        }

        return new MemoryRetrievalResult(entry, score, string.Join(", ", reasons.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<string> ExtractTerms(string text)
    {
        var normalized = text.ToLowerInvariant();
        var splitTerms = normalized
            .Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '/', '\\', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 3)
            .ToList();
        var compact = new string(normalized.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        var cjkTerms = ExtractCjkNgrams(compact);
        return splitTerms
            .Concat(cjkTerms)
            .Select(term => term.Trim('-', '_'))
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> ExtractCjkNgrams(string text)
    {
        var cjk = new string(text.Where(IsCjk).ToArray());
        if (cjk.Length < 2)
        {
            return [];
        }

        var terms = new List<string>();
        for (var size = 2; size <= Math.Min(4, cjk.Length); size++)
        {
            for (var i = 0; i <= cjk.Length - size; i++)
            {
                terms.Add(cjk.Substring(i, size));
            }
        }

        return terms;
    }

    private static bool IsCjk(char ch)
    {
        return ch is >= '\u4e00' and <= '\u9fff';
    }

    private static bool IsVerificationFailureMemory(MemoryEntry entry)
    {
        return entry.Category == MemoryCategory.Tool &&
               entry.Metadata.TryGetValue("kind", out var kind) &&
               string.Equals(kind, "verification-failure", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeVerificationOrRepairQuery(IReadOnlyList<string> terms)
    {
        var joined = string.Join(' ', terms);
        return joined.Contains("test", StringComparison.OrdinalIgnoreCase) ||
               joined.Contains("build", StringComparison.OrdinalIgnoreCase) ||
               joined.Contains("verify", StringComparison.OrdinalIgnoreCase) ||
               joined.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
               joined.Contains("修复", StringComparison.OrdinalIgnoreCase) ||
               joined.Contains("验证", StringComparison.OrdinalIgnoreCase) ||
               joined.Contains("测试", StringComparison.OrdinalIgnoreCase) ||
               joined.Contains("失败", StringComparison.OrdinalIgnoreCase);
    }
}
