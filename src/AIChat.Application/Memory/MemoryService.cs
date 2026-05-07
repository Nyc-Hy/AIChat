using AIChat.Domain.Memory;

namespace AIChat.Application.Memory;

public sealed class MemoryService
{
    private static readonly string[] SecretPatterns =
    [
        "api_key",
        "apikey",
        "secret",
        "password",
        "passwd",
        "token",
        "bearer ",
        "sk-",
        "xoxb-",
        "ghp_"
    ];

    public MemoryWriteResult TryCreate(MemoryWriteRequest request)
    {
        var content = request.Content.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return MemoryWriteResult.Rejected("empty memory");
        }

        if (ContainsSecret(content))
        {
            return MemoryWriteResult.Rejected("content appears to contain a secret");
        }

        if (request.Category == MemoryCategory.User && !request.UserConfirmed && !IsSafeUserMemory(content))
        {
            return MemoryWriteResult.Rejected("user memory requires confirmation");
        }

        var now = DateTimeOffset.Now;
        return MemoryWriteResult.Stored(new MemoryEntry
        {
            ProjectId = request.ProjectId,
            Category = request.Category,
            Content = content,
            Source = string.IsNullOrWhiteSpace(request.Source) ? "agent" : request.Source.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            Metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        });
    }

    public MemoryWriteResult Add(ICollection<MemoryEntry> entries, MemoryWriteRequest request)
    {
        var result = TryCreate(request);
        if (!result.IsStored || result.Entry is null)
        {
            return result;
        }

        entries.Add(result.Entry);
        return result;
    }

    public static bool ContainsSecret(string content)
    {
        return SecretPatterns.Any(pattern => content.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafeUserMemory(string content)
    {
        var normalized = content.ToLowerInvariant();
        return normalized.Contains("prefers", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("喜欢", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("偏好", StringComparison.OrdinalIgnoreCase);
    }
}
