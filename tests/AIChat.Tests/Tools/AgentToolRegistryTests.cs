using AIChat.Abstractions.Configuration;
using AIChat.Application.Tools;

namespace AIChat.Tests.Tools;

public sealed class AgentToolRegistryTests
{
    [Fact]
    public void CreateDefault_ContainsAllBuiltinTools()
    {
        var registry = AgentToolRegistry.CreateDefault();

        Assert.Equal(15, registry.All.Count);
        Assert.NotNull(registry.Find("read_input_artifact"));
        Assert.NotNull(registry.Find("read_file"));
        Assert.NotNull(registry.Find("write_file"));
        Assert.NotNull(registry.Find("run_shell"));
        Assert.NotNull(registry.Find("git_status"));
    }

    [Fact]
    public void GetMetadata_ReturnsCorrectCategory()
    {
        var registry = AgentToolRegistry.CreateDefault();

        var readMeta = registry.GetMetadata("read_file");
        Assert.Equal("读取", readMeta.Category);

        var artifactMeta = registry.GetMetadata("read_input_artifact");
        Assert.Equal("读取", artifactMeta.Category);

        var writeMeta = registry.GetMetadata("write_file");
        Assert.Equal("写入", writeMeta.Category);

        var gitMeta = registry.GetMetadata("git_status");
        Assert.Equal("Git", gitMeta.Category);

        var shellMeta = registry.GetMetadata("run_shell");
        Assert.Equal("Shell", shellMeta.Category);
    }

    [Fact]
    public void GetMetadata_ReturnsCorrectDefaultPermissionMode()
    {
        var registry = AgentToolRegistry.CreateDefault();

        Assert.Equal(ToolPermissionMode.AutoReadOnly, registry.GetMetadata("read_file").DefaultPermissionMode);
        Assert.Equal(ToolPermissionMode.AutoReadOnly, registry.GetMetadata("read_input_artifact").DefaultPermissionMode);
        Assert.Equal(ToolPermissionMode.ConfirmEachTime, registry.GetMetadata("write_file").DefaultPermissionMode);
        Assert.Equal(ToolPermissionMode.ConfirmEachTime, registry.GetMetadata("run_shell").DefaultPermissionMode);
    }

    [Fact]
    public void GetMetadata_ReturnsFallbackForUnknownTool()
    {
        var registry = AgentToolRegistry.CreateDefault();

        var meta = registry.GetMetadata("nonexistent_tool");

        Assert.Equal("nonexistent_tool", meta.ToolId);
        Assert.Equal("通用", meta.Category);
        Assert.Equal(ToolPermissionMode.ConfirmEachTime, meta.DefaultPermissionMode);
    }

    [Fact]
    public void ResolveEnabled_FiltersCorrectly()
    {
        var registry = AgentToolRegistry.CreateDefault();

        var enabled = registry.ResolveEnabled(["read_file", "git_status"]);

        Assert.Equal(2, enabled.Count);
        Assert.Contains(enabled, t => t.Id == "read_file");
        Assert.Contains(enabled, t => t.Id == "git_status");
    }

    [Fact]
    public void AllWithMetadata_ReturnsPairsForAllTools()
    {
        var registry = AgentToolRegistry.CreateDefault();

        var pairs = registry.AllWithMetadata();

        Assert.Equal(15, pairs.Count);
        Assert.All(pairs, p => Assert.NotNull(p.Metadata));
        Assert.All(pairs, p => Assert.Equal(p.Tool.Id, p.Metadata.ToolId));
    }
}
