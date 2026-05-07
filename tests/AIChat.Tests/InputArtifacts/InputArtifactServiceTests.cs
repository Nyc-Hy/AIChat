using AIChat.Application.Artifacts;
using AIChat.Domain.Artifacts;

namespace AIChat.Tests.Artifacts;

public sealed class InputArtifactServiceTests
{
    [Fact]
    public void Create_TextArtifactBuildsSummaryAndInspectableRef()
    {
        var artifact = new InputArtifactService().Create(new InputArtifactCreateRequest
        {
            ProjectId = "project-1",
            ConversationId = "conversation-1",
            MessageId = "message-1",
            FileName = "notes.md",
            MimeType = "text/markdown",
            ContentText = "These notes describe the login failure and the expected behavior."
        });

        Assert.Equal(InputArtifactKind.Text, artifact.Kind);
        Assert.Equal("project-1", artifact.ProjectId);
        Assert.Contains("login failure", artifact.RawText);
        Assert.Contains("login failure", artifact.Summary);
        Assert.StartsWith("input-artifact:", artifact.RefId);
        Assert.Equal(artifact.RefId, artifact.Metadata["ref"]);
        Assert.Equal("md", artifact.Metadata["extension"]);
    }

    [Fact]
    public void Create_ImageArtifactFallsBackToMetadataSummary()
    {
        var artifact = new InputArtifactService().Create(new InputArtifactCreateRequest
        {
            FileName = "screen.png",
            MimeType = "image/png"
        });

        Assert.Equal(InputArtifactKind.Image, artifact.Kind);
        Assert.Empty(artifact.RawText);
        Assert.Contains("no OCR or image description", artifact.Summary);
        Assert.Contains("image/png", artifact.Metadata["mimeType"]);
    }

    [Fact]
    public void Create_DocumentWithTextBuildsDocumentSummary()
    {
        var artifact = new InputArtifactService().Create(new InputArtifactCreateRequest
        {
            FileName = "requirements.pdf",
            MimeType = "application/pdf",
            ContentText = "The agent should inspect the failing tests before editing code."
        });

        Assert.Equal(InputArtifactKind.Document, artifact.Kind);
        Assert.Contains("requirements.pdf", artifact.Summary);
        Assert.Contains("failing tests", artifact.Summary);
    }

    [Fact]
    public void BuildPromptRefs_ReturnsStableArtifactReference()
    {
        var service = new InputArtifactService();
        var artifact = service.Create(new InputArtifactCreateRequest
        {
            FileName = "data.csv",
            MimeType = "text/csv",
            ContentText = "name,count"
        });

        var promptRef = Assert.Single(service.BuildPromptRefs([artifact]));

        Assert.Contains(artifact.RefId, promptRef);
        Assert.Contains("[Spreadsheet]", promptRef);
        Assert.Contains("data.csv", promptRef);
    }
}
