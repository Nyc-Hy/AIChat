using AIChat.Application.Memory;
using AIChat.Domain.Memory;

namespace AIChat.Tests.Memory;

public sealed class MemoryServiceTests
{
    [Fact]
    public void TryCreate_StoresProjectMemoryWithSourceAndTimestamp()
    {
        var result = new MemoryService().TryCreate(new MemoryWriteRequest
        {
            ProjectId = "project-1",
            Category = MemoryCategory.Project,
            Content = "Use MVVM patterns for WPF view models.",
            Source = "AGENTS.md"
        });

        Assert.True(result.IsStored);
        Assert.NotNull(result.Entry);
        Assert.Equal("project-1", result.Entry!.ProjectId);
        Assert.Equal(MemoryCategory.Project, result.Entry.Category);
        Assert.Equal("AGENTS.md", result.Entry.Source);
        Assert.True(result.Entry.CreatedAt <= DateTimeOffset.Now);
    }

    [Theory]
    [InlineData("api_key=abc123")]
    [InlineData("password: hunter2")]
    [InlineData("Bearer token-value")]
    [InlineData("sk-secret")]
    public void TryCreate_RejectsLikelySecrets(string content)
    {
        var result = new MemoryService().TryCreate(new MemoryWriteRequest
        {
            ProjectId = "project-1",
            Category = MemoryCategory.Project,
            Content = content
        });

        Assert.False(result.IsStored);
        Assert.Contains("secret", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreate_RequiresConfirmationForUnsafeUserMemory()
    {
        var result = new MemoryService().TryCreate(new MemoryWriteRequest
        {
            ProjectId = "project-1",
            Category = MemoryCategory.User,
            Content = "The user lives in Shanghai."
        });

        Assert.False(result.IsStored);
        Assert.Contains("confirmation", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreate_AllowsConfirmedUserMemory()
    {
        var result = new MemoryService().TryCreate(new MemoryWriteRequest
        {
            ProjectId = "project-1",
            Category = MemoryCategory.User,
            Content = "The user prefers concise Chinese responses.",
            UserConfirmed = true
        });

        Assert.True(result.IsStored);
        Assert.Equal(MemoryCategory.User, result.Entry!.Category);
    }
}
