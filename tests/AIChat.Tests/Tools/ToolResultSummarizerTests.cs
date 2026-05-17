using AIChat.Application.Tools;
using System.Text.Json;

namespace AIChat.Tests.Tools;

public sealed class ToolResultSummarizerTests
{
    [Fact]
    public void Summarize_KeepsSmallResultUnchanged()
    {
        var result = new AgentToolResult
        {
            ToolName = "read_file",
            Content = "short"
        };

        var summarized = ToolResultSummarizer.Summarize(result);

        Assert.Same(result, summarized);
        Assert.Equal("short", summarized.ContentForModel);
        Assert.False(summarized.WasSummarized);
    }

    [Fact]
    public void Summarize_StoresRawContentAndCreatesModelContentForLargeResult()
    {
        var content = string.Join('\n', Enumerable.Range(1, 120).Select(i => $"line {i:000}"));
        var result = new AgentToolResult
        {
            ToolName = "run_shell",
            Content = content
        };

        var summarized = ToolResultSummarizer.Summarize(result, threshold: 200);

        Assert.True(summarized.WasSummarized);
        Assert.Equal(content, summarized.Content);
        Assert.NotEqual(content, summarized.ContentForModel);
        Assert.Contains("原文已保存为运行产物", summarized.ContentForModel);
        Assert.Contains("line 001", summarized.ContentForModel);
        Assert.Contains("line 120", summarized.ContentForModel);
        Assert.Equal("tool_result", summarized.ArtifactKind);
    }

    [Fact]
    public void Summarize_ExtractsCommandMetadataFromJsonOutput()
    {
        var output = string.Join('\n', Enumerable.Range(1, 80).Select(i => $"build line {i:000}"));
        var content = JsonSerializer.Serialize(new
        {
            command = "dotnet test",
            exitCode = 1,
            timedOut = false,
            output
        });

        var summarized = ToolResultSummarizer.Summarize(new AgentToolResult
        {
            ToolName = "run_test",
            Content = content,
            IsError = true
        }, threshold: 200);

        Assert.True(summarized.WasSummarized);
        Assert.Contains("命令：dotnet test", summarized.ContentForModel);
        Assert.Contains("退出码：1", summarized.ContentForModel);
        Assert.Contains("build line 001", summarized.ContentForModel);
    }

    [Fact]
    public void Summarize_IncludesFocusedErrorBlocksForLargeOutput()
    {
        var content = string.Join('\n',
            Enumerable.Range(1, 160).Select(i => i == 100
                ? "AppTests.cs(42): error CS1002: ; expected"
                : $"line {i:000}"));

        var summarized = ToolResultSummarizer.Summarize(new AgentToolResult
        {
            ToolName = "run_test",
            Content = content,
            IsError = true
        }, threshold: 200);

        Assert.True(summarized.WasSummarized);
        Assert.Contains("关键片段", summarized.ContentForModel);
        Assert.Contains("error CS1002", summarized.ContentForModel);
        Assert.Contains("已省略约", summarized.ContentForModel);
    }
}
