using System.Text.Json;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Tools;

public sealed class UpdatePlanToolTests
{
    private readonly UpdatePlanTool _tool = new();
    private readonly AgentToolContext _context = new() { ProjectPath = "/tmp" };

    [Fact]
    public void Definition_HasCorrectIdAndRisk()
    {
        Assert.Equal("update_plan", _tool.Id);
        Assert.Equal(AgentToolRisk.ReadOnly, _tool.Risk);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccess_WithValidArguments()
    {
        var args = JsonSerializer.Serialize(new
        {
            summary = "Fix the bug",
            items = new[]
            {
                new { title = "Read code", status = "completed", notes = "" },
                new { title = "Fix it", status = "in_progress", notes = "Working on it" }
            }
        });

        var result = await _tool.ExecuteAsync(args, _context);

        Assert.False(result.IsError);
        Assert.Equal("update_plan", result.ToolName);

        using var doc = JsonDocument.Parse(result.Content);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Fix the bug", root.GetProperty("summary").GetString());
        Assert.Equal(2, root.GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_WhenSummaryMissing()
    {
        var args = JsonSerializer.Serialize(new
        {
            items = new[] { new { title = "Task 1", status = "pending", notes = "" } }
        });

        var result = await _tool.ExecuteAsync(args, _context);

        Assert.True(result.IsError);
        Assert.Contains("summary", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSuccess_WhenUsingItemSingular()
    {
        var args = JsonSerializer.Serialize(new
        {
            summary = "Fix the bug",
            item = new[]
            {
                new { title = "Read code", status = "completed", notes = "" }
            }
        });

        var result = await _tool.ExecuteAsync(args, _context);

        Assert.False(result.IsError);
        using var doc = JsonDocument.Parse(result.Content);
        Assert.Equal(1, doc.RootElement.GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_WhenItemsMissing()
    {
        var args = JsonSerializer.Serialize(new
        {
            summary = "Some plan"
        });

        var result = await _tool.ExecuteAsync(args, _context);

        Assert.True(result.IsError);
        Assert.Contains("items", result.Content);
    }

    [Theory]
    [InlineData("pending", AgentPlanItemStatus.Pending)]
    [InlineData("in_progress", AgentPlanItemStatus.InProgress)]
    [InlineData("completed", AgentPlanItemStatus.Completed)]
    [InlineData("blocked", AgentPlanItemStatus.Blocked)]
    [InlineData("skipped", AgentPlanItemStatus.Skipped)]
    [InlineData("PENDING", AgentPlanItemStatus.Pending)]
    [InlineData("unknown", AgentPlanItemStatus.Pending)]
    [InlineData("", AgentPlanItemStatus.Pending)]
    [InlineData(null, AgentPlanItemStatus.Pending)]
    public void ParseStatus_MapsCorrectly(string? input, AgentPlanItemStatus expected)
    {
        Assert.Equal(expected, UpdatePlanTool.ParseStatus(input!));
    }

    [Fact]
    public async Task PreviewAsync_ReturnsSummary()
    {
        var args = JsonSerializer.Serialize(new { summary = "My plan", items = new[] { new { title = "Task 1", status = "pending", notes = "" } } });
        var preview = await _tool.PreviewAsync(args, _context);

        Assert.Equal("update_plan", preview.ToolName);
        Assert.Equal(AgentToolRisk.ReadOnly, preview.Risk);
        Assert.Contains("My plan", preview.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesEmptyArguments()
    {
        var result = await _tool.ExecuteAsync("", _context);
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesMalformedJson()
    {
        var result = await _tool.ExecuteAsync("{bad json", _context);
        Assert.True(result.IsError);
    }
}
