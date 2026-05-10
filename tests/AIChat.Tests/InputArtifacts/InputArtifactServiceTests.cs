using AIChat.Application.Artifacts;
using AIChat.Domain.Artifacts;
using System.IO.Compression;
using System.Text;

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
    public void Create_DocxArtifactExtractsDocumentTextFromBytes()
    {
        var artifact = new InputArtifactService().Create(new InputArtifactCreateRequest
        {
            FileName = "requirements.docx",
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            FileBytes = CreateZip(("word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>Login must show a clear validation message.</w:t></w:r></w:p>
                    <w:p><w:r><w:t>Retry should keep the user's email.</w:t></w:r></w:p>
                  </w:body>
                </w:document>
                """))
        });

        Assert.Equal(InputArtifactKind.Document, artifact.Kind);
        Assert.Contains("validation message", artifact.RawText);
        Assert.Contains("Retry should keep", artifact.RawText);
        Assert.Equal("text", artifact.Metadata["extraction"]);
        Assert.DoesNotContain("no document text", artifact.Summary);
    }

    [Fact]
    public void Create_XlsxArtifactExtractsRowsFromSharedStrings()
    {
        var artifact = new InputArtifactService().Create(new InputArtifactCreateRequest
        {
            FileName = "cases.xlsx",
            MimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileBytes = CreateZip(
                ("xl/sharedStrings.xml", """
                    <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                      <si><t>scenario</t></si>
                      <si><t>expected</t></si>
                      <si><t>login failure</t></si>
                      <si><t>error banner</t></si>
                    </sst>
                    """),
                ("xl/worksheets/sheet1.xml", """
                    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                      <sheetData>
                        <row><c t="s"><v>0</v></c><c t="s"><v>1</v></c></row>
                        <row><c t="s"><v>2</v></c><c t="s"><v>3</v></c></row>
                      </sheetData>
                    </worksheet>
                    """))
        });

        Assert.Equal(InputArtifactKind.Spreadsheet, artifact.Kind);
        Assert.Contains("scenario\texpected", artifact.RawText);
        Assert.Contains("login failure\terror banner", artifact.RawText);
        Assert.Equal("text", artifact.Metadata["extraction"]);
    }

    [Fact]
    public void Create_PdfArtifactExtractsSimpleTextOperators()
    {
        var bytes = Encoding.Latin1.GetBytes("""
            %PDF-1.4
            1 0 obj
            BT
            (Checkout should preserve cart items) Tj
            [(Total ) 120 (must update)] TJ
            ET
            endobj
            """);

        var artifact = new InputArtifactService().Create(new InputArtifactCreateRequest
        {
            FileName = "spec.pdf",
            MimeType = "application/pdf",
            FileBytes = bytes
        });

        Assert.Equal(InputArtifactKind.Document, artifact.Kind);
        Assert.Contains("Checkout should preserve cart items", artifact.RawText);
        Assert.Contains("Total must update", artifact.RawText);
        Assert.Equal("text", artifact.Metadata["extraction"]);
    }

    [Fact]
    public void GetDetail_IncludesExtractedRawTextForDocument()
    {
        var artifact = new InputArtifactService().Create(new InputArtifactCreateRequest
        {
            FileName = "requirements.docx",
            MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            FileBytes = CreateZip(("word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body><w:p><w:r><w:t>Use the provided acceptance criteria.</w:t></w:r></w:p></w:body>
                </w:document>
                """))
        });

        var detail = new InputArtifactService().GetDetail(artifact);

        Assert.Contains("Raw text:", detail);
        Assert.Contains("acceptance criteria", detail);
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

    [Fact]
    public void Prune_KeepsNewestArtifactsPerConversationAndProjectLevel()
    {
        var service = new InputArtifactService();
        var artifacts = new List<InputArtifact>
        {
            Artifact("old-a", "conversation-a", 1),
            Artifact("new-a", "conversation-a", 2),
            Artifact("old-b", "conversation-b", 1),
            Artifact("new-b", "conversation-b", 2),
            Artifact("old-project", "", 1),
            Artifact("new-project", "", 2)
        };

        var removed = service.Prune(artifacts, new InputArtifactCleanupOptions
        {
            MaxArtifactsPerConversation = 1,
            MaxProjectLevelArtifacts = 1
        });

        Assert.Equal(3, removed);
        Assert.Equal(["new-a", "new-b", "new-project"], artifacts.Select(item => item.Id).Order());
    }

    [Fact]
    public void PruneRemoved_ReturnsRemovedArtifactsForExternalCleanup()
    {
        var service = new InputArtifactService();
        var artifacts = new List<InputArtifact>
        {
            Artifact("old-a", "conversation-a", 1),
            Artifact("new-a", "conversation-a", 2)
        };

        var removed = service.PruneRemoved(artifacts, new InputArtifactCleanupOptions
        {
            MaxArtifactsPerConversation = 1
        });

        var removedArtifact = Assert.Single(removed);
        Assert.Equal("old-a", removedArtifact.Id);
        Assert.Equal(["new-a"], artifacts.Select(item => item.Id));
    }

    [Fact]
    public void RemoveForConversation_RemovesMatchingArtifactsAndReturnsThem()
    {
        var service = new InputArtifactService();
        var artifacts = new List<InputArtifact>
        {
            Artifact("conversation-a-1", "conversation-a", 1),
            Artifact("conversation-b-1", "conversation-b", 1),
            Artifact("conversation-a-2", "CONVERSATION-A", 2),
            Artifact("project-level", "", 3)
        };

        var removed = service.RemoveForConversation(artifacts, "conversation-a");

        Assert.Equal(["conversation-a-1", "conversation-a-2"], removed.Select(item => item.Id).Order());
        Assert.Equal(["conversation-b-1", "project-level"], artifacts.Select(item => item.Id).Order());
    }

    [Fact]
    public void RemoveForConversation_IgnoresBlankConversationId()
    {
        var service = new InputArtifactService();
        var artifacts = new List<InputArtifact>
        {
            Artifact("project-level", "", 1)
        };

        var removed = service.RemoveForConversation(artifacts, "");

        Assert.Empty(removed);
        Assert.Single(artifacts);
    }

    private static InputArtifact Artifact(string id, string conversationId, int day)
    {
        return new InputArtifact
        {
            Id = id,
            ConversationId = conversationId,
            CreatedAt = DateTimeOffset.Parse($"2026-01-{day:00}T00:00:00Z"),
            Summary = id
        };
    }

    private static byte[] CreateZip(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}
