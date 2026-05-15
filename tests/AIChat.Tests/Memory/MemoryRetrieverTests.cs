using AIChat.Application.Memory;
using AIChat.Domain.Memory;

namespace AIChat.Tests.Memory;

public sealed class MemoryRetrieverTests
{
    [Fact]
    public void Retrieve_FiltersByCategoryAndRanksQueryMatches()
    {
        var entries = new List<MemoryEntry>
        {
            new() { ProjectId = "p1", Category = MemoryCategory.Project, Content = "Authentication uses token refresh flow.", Source = "arch" },
            new() { ProjectId = "p1", Category = MemoryCategory.Task, Content = "Login tests fail around refresh token expiry.", Source = "run" },
            new() { ProjectId = "p1", Category = MemoryCategory.User, Content = "Prefers short answers.", Source = "user" },
            new() { ProjectId = "p2", Category = MemoryCategory.Project, Content = "Other project login info.", Source = "other" }
        };

        var results = new MemoryRetriever().Retrieve(entries, new MemoryRetrievalRequest
        {
            ProjectId = "p1",
            Query = "fix login refresh token",
            Categories = new HashSet<MemoryCategory> { MemoryCategory.Project, MemoryCategory.Task },
            MaxResults = 3
        });

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, result => result.Entry.Category == MemoryCategory.User);
        Assert.DoesNotContain(results, result => result.Entry.ProjectId == "p2");
        Assert.Contains("refresh", results[0].Entry.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(results[0].Score >= results[1].Score);
    }

    [Fact]
    public void RetrieveSnippets_FormatsEntriesForPromptUse()
    {
        var snippets = new MemoryRetriever().RetrieveSnippets(
            [
                new MemoryEntry
                {
                    ProjectId = "p1",
                    Category = MemoryCategory.Project,
                    Content = "Use apply_patch for edits.",
                    Source = "policy"
                }
            ],
            new MemoryRetrievalRequest { ProjectId = "p1", Query = "apply patch" });

        var snippet = Assert.Single(snippets);
        Assert.Contains("[Project]", snippet);
        Assert.Contains("Use apply_patch", snippet);
        Assert.Contains("policy", snippet);
    }

    [Fact]
    public void Retrieve_BoostsFailedVerificationMemoryForChineseRepairQuery()
    {
        var entries = new List<MemoryEntry>
        {
            new()
            {
                ProjectId = "p1",
                Category = MemoryCategory.Tool,
                Content = "Verification failure for \"修复测试\": \"dotnet test\" exit 1. Summary: AppTests.cs(12): error CS1002. Related files: src/App.cs.",
                Source = "agent-run:r1",
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "verification-failure"
                }
            },
            new()
            {
                ProjectId = "p1",
                Category = MemoryCategory.Project,
                Content = "Use MVVM patterns for view models.",
                Source = "arch"
            }
        };

        var results = new MemoryRetriever().Retrieve(entries, new MemoryRetrievalRequest
        {
            ProjectId = "p1",
            Query = "继续修复测试失败，重跑 dotnet test",
            MaxResults = 2
        });

        Assert.Equal("verification-failure", results[0].Entry.Metadata["kind"]);
        Assert.Contains("verification-failure", results[0].Reason);
    }
}
