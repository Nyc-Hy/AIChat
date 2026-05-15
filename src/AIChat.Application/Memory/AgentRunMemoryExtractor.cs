using AIChat.Domain.Chat;
using AIChat.Domain.Memory;

namespace AIChat.Application.Memory;

public sealed class AgentRunMemoryExtractor
{
    public IReadOnlyList<MemoryCandidate> Extract(Conversation conversation, AgentRun run)
    {
        var failedVerifications = run.Verifications
            .Where(verification => !verification.IsSuccess)
            .Take(3)
            .ToList();
        if (run.Status != AgentRunStatus.Completed && failedVerifications.Count == 0)
        {
            return [];
        }

        var candidates = new List<MemoryCandidate>();
        var source = $"agent-run:{run.Id}";
        var goal = NormalizeWhitespace(run.Goal);
        var shortGoal = Truncate(goal, 90);
        var changedPaths = run.FileChanges
            .Select(change => change.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (run.Status == AgentRunStatus.Completed &&
            !string.IsNullOrWhiteSpace(shortGoal) &&
            changedPaths.Count > 0)
        {
            candidates.Add(new MemoryCandidate
            {
                Category = MemoryCategory.Task,
                Source = source,
                Content = $"Task \"{shortGoal}\" changed: {string.Join(", ", changedPaths)}.",
                Metadata = CreateMetadata(run, "task-files")
            });
            candidates.Add(new MemoryCandidate
            {
                Category = MemoryCategory.Project,
                Source = source,
                Content = $"Files relevant to \"{shortGoal}\": {string.Join(", ", changedPaths)}.",
                Metadata = CreateMetadata(run, "project-files")
            });
        }

        foreach (var verification in run.Verifications.Take(3))
        {
            if (string.IsNullOrWhiteSpace(verification.Command))
            {
                continue;
            }

            var result = verification.IsSuccess ? "passed" : verification.TimedOut ? "timed out" : "failed";
            var summary = string.IsNullOrWhiteSpace(verification.Summary)
                ? ""
                : $" Summary: {Truncate(NormalizeWhitespace(verification.Summary), 120)}";
            candidates.Add(new MemoryCandidate
            {
                Category = MemoryCategory.Tool,
                Source = source,
                Content = $"Verification command \"{verification.Command}\" {result} for \"{shortGoal}\".{summary}",
                Metadata = CreateMetadata(run, "verification")
            });
        }

        foreach (var verification in failedVerifications)
        {
            if (string.IsNullOrWhiteSpace(verification.Command))
            {
                continue;
            }

            var summary = string.IsNullOrWhiteSpace(verification.Summary)
                ? Truncate(NormalizeWhitespace(verification.Output), 160)
                : Truncate(NormalizeWhitespace(verification.Summary), 160);
            var related = changedPaths.Count == 0 ? "" : $" Related files: {string.Join(", ", changedPaths.Take(5))}.";
            candidates.Add(new MemoryCandidate
            {
                Category = MemoryCategory.Tool,
                Source = source,
                Content = $"Verification failure for \"{shortGoal}\": \"{verification.Command}\" exit {verification.ExitCode}.{(verification.TimedOut ? " Timed out." : "")} Summary: {summary}.{related}",
                Metadata = CreateMetadata(run, "verification-failure")
            });
        }

        foreach (var step in run.Steps.Where(step => step.IsError && !string.IsNullOrWhiteSpace(step.ToolName)).Take(3))
        {
            candidates.Add(new MemoryCandidate
            {
                Category = MemoryCategory.Tool,
                Source = source,
                Content = $"Tool \"{step.ToolName}\" hit an error during \"{shortGoal}\": {Truncate(NormalizeWhitespace(step.Output), 140)}",
                Metadata = CreateMetadata(run, "tool-error")
            });
        }

        if (run.Status == AgentRunStatus.Completed)
        {
            candidates.AddRange(ExtractUserPreferenceCandidates(conversation, run, source));
        }
        return candidates
            .Where(candidate => !MemoryService.ContainsSecret(candidate.Content))
            .GroupBy(candidate => NormalizeKey(candidate.Category, candidate.Content), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)
            .ToList();
    }

    private static IEnumerable<MemoryCandidate> ExtractUserPreferenceCandidates(Conversation conversation, AgentRun run, string source)
    {
        var userMessages = conversation.Messages
            .Where(message => message.Role == ChatRole.User)
            .OrderByDescending(message => message.CreatedAt)
            .Take(6);

        foreach (var message in userMessages)
        {
            var content = NormalizeWhitespace(message.Content);
            if (!LooksLikePreference(content))
            {
                continue;
            }

            yield return new MemoryCandidate
            {
                Category = MemoryCategory.User,
                Source = source,
                RequiresUserConfirmation = true,
                Content = Truncate(content, 180),
                Metadata = CreateMetadata(run, "user-preference")
            };
        }
    }

    private static bool LooksLikePreference(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return content.Contains("我喜欢", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("我希望", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("我的偏好", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("以后", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("prefer", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("preference", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> CreateMetadata(AgentRun run, string kind)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["runId"] = run.Id,
            ["conversationId"] = run.ConversationId,
            ["kind"] = kind,
            ["status"] = run.Status.ToString()
        };
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(' ', value.ReplaceLineEndings(" ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeKey(MemoryCategory category, string content)
    {
        return $"{category}:{NormalizeWhitespace(content).ToLowerInvariant()}";
    }

    private static string Truncate(string value, int maxChars)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }
}
