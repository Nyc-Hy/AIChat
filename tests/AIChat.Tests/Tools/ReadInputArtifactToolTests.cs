using AIChat.Application.Tools;
using AIChat.Domain.Artifacts;

namespace AIChat.Tests.Tools;

public sealed class ReadInputArtifactToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsArtifactDetailByRef()
    {
        var tool = new ReadInputArtifactTool();
        var artifact = new InputArtifact
        {
            Id = "artifact-1",
            Kind = InputArtifactKind.Document,
            FileName = "spec.pdf",
            MimeType = "application/pdf",
            Summary = "Spec summary",
            RawText = "Full extracted document text."
        };

        var result = await tool.ExecuteAsync(
            """{"ref":"input-artifact:artifact-1"}""",
            new AgentToolContext
            {
                ProjectPath = Environment.CurrentDirectory,
                InputArtifacts = [artifact]
            });

        Assert.False(result.IsError);
        Assert.Contains("input-artifact:artifact-1", result.Content);
        Assert.Contains("Spec summary", result.Content);
        Assert.Contains("Full extracted document text", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_AcceptsBareArtifactId()
    {
        var tool = new ReadInputArtifactTool();

        var result = await tool.ExecuteAsync(
            """{"ref":"artifact-2"}""",
            new AgentToolContext
            {
                ProjectPath = Environment.CurrentDirectory,
                InputArtifacts =
                [
                    new InputArtifact
                    {
                        Id = "artifact-2",
                        Kind = InputArtifactKind.Image,
                        FileName = "ui.png",
                        Summary = "Image summary"
                    }
                ]
            });

        Assert.False(result.IsError);
        Assert.Contains("ui.png", result.Content);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsErrorForMissingRef()
    {
        var result = await new ReadInputArtifactTool().ExecuteAsync(
            """{"ref":"input-artifact:missing"}""",
            new AgentToolContext
            {
                ProjectPath = Environment.CurrentDirectory,
                InputArtifacts = []
            });

        Assert.True(result.IsError);
        Assert.Contains("未找到", result.Content);
    }

    [Fact]
    public async Task PreviewAsync_IsReadOnly()
    {
        var preview = await new ReadInputArtifactTool().PreviewAsync(
            """{"ref":"input-artifact:abc"}""",
            new AgentToolContext { ProjectPath = Environment.CurrentDirectory });

        Assert.Equal(AgentToolRisk.ReadOnly, preview.Risk);
        Assert.Contains("input-artifact:abc", preview.Summary);
    }
}
