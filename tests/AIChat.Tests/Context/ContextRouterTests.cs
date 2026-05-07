using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Context;
using AIChat.Application.Workspace;
using AIChat.Domain.Artifacts;
using AIChat.Domain.Chat;
using AIChat.Domain.Context;

namespace AIChat.Tests.Context;

public sealed class ContextRouterTests
{
    [Fact]
    public void Route_PrioritizesGoalPinnedAndRecentEditMatches()
    {
        var pack = new ContextRouter().Route(new ContextRouterRequest
        {
            Goal = "fix login service bug",
            Phase = AgentRunPhase.Executing,
            FileIndex = new ProjectFileIndex
            {
                Entries =
                [
                    new ProjectFileIndexEntry { RelativePath = "src/Auth/LoginService.cs", TypeTag = "source", SizeBytes = 4000 },
                    new ProjectFileIndexEntry { RelativePath = "docs/Architecture.md", TypeTag = "doc", SizeBytes = 2000 },
                    new ProjectFileIndexEntry { RelativePath = "src/Unrelated.cs", TypeTag = "source", SizeBytes = 1000 }
                ]
            },
            PinnedItems = [new PinnedContextItem { Path = "docs/Architecture.md", Note = "auth overview" }],
            WorkspaceChanges = [new WorkspaceChange { Path = "src/Auth/LoginService.cs", Status = " M" }],
            MaxTokens = 1000
        });

        Assert.Contains(pack.IncludedFiles, file => file.Path == "src/Auth/LoginService.cs");
        Assert.Contains(pack.IncludedFiles, file => file.Path == "docs/Architecture.md");
        Assert.Equal("src/Auth/LoginService.cs", pack.IncludedFiles[0].Path);
        Assert.Contains("Context pack:", pack.Summary);
        Assert.True(pack.EstimatedTokens > 0);
    }

    [Fact]
    public void Route_DoesNotIncludeLargeFilesBlindly()
    {
        var pack = new ContextRouter().Route(new ContextRouterRequest
        {
            Goal = "update generated client",
            Phase = AgentRunPhase.Executing,
            FileIndex = new ProjectFileIndex
            {
                Entries =
                [
                    new ProjectFileIndexEntry { RelativePath = "src/Generated/Client.cs", TypeTag = "source", SizeBytes = 900_000 },
                    new ProjectFileIndexEntry { RelativePath = "src/ClientFacade.cs", TypeTag = "source", SizeBytes = 2000 }
                ]
            },
            MaxFileSizeBytes = 100_000,
            MaxTokens = 1000
        });

        Assert.DoesNotContain(pack.IncludedFiles, file => file.Path == "src/Generated/Client.cs");
        Assert.Contains(pack.OmittedButRelevantRefs, file => file.Path == "src/Generated/Client.cs");
    }

    [Fact]
    public void Route_TrimsRelevantFilesToBudget()
    {
        var entries = Enumerable.Range(1, 40)
            .Select(index => new ProjectFileIndexEntry
            {
                RelativePath = $"src/Login/Feature{index}.cs",
                TypeTag = "source",
                SizeBytes = 20_000
            })
            .ToList();

        var pack = new ContextRouter().Route(new ContextRouterRequest
        {
            Goal = "login feature",
            Phase = AgentRunPhase.Executing,
            FileIndex = new ProjectFileIndex { Entries = entries },
            MaxTokens = 260
        });

        Assert.True(pack.IncludedFiles.Count > 0);
        Assert.True(pack.OmittedButRelevantRefs.Count > 0);
        Assert.True(pack.EstimatedTokens <= 320);
    }

    [Fact]
    public void Route_IncludesArtifactRefsAndPinnedSnippets()
    {
        var pack = new ContextRouter().Route(new ContextRouterRequest
        {
            Goal = "repair tests",
            Phase = AgentRunPhase.Repairing,
            PinnedItems = [new PinnedContextItem { Path = "tests/AuthTests.cs", StartLine = 12, EndLine = 30, Note = "failing test" }],
            Artifacts =
            [
                new AgentArtifact { ToolName = "run_test", Kind = "tool_result", Summary = "AuthTests failed" }
            ],
            MaxTokens = 500
        });

        Assert.Contains(pack.IncludedSnippets, snippet => snippet.Contains("tests/AuthTests.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(pack.ArtifactRefs, artifact => artifact.Contains("AuthTests failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Route_IncludesInputArtifactRefs()
    {
        var pack = new ContextRouter().Route(new ContextRouterRequest
        {
            Goal = "explain attached screenshot",
            Phase = AgentRunPhase.GatheringContext,
            InputArtifacts =
            [
                new InputArtifact
                {
                    Id = "artifact-1",
                    Kind = InputArtifactKind.Screenshot,
                    FileName = "checkout-screenshot.png",
                    MimeType = "image/png",
                    Summary = "Screenshot shows checkout button disabled."
                }
            ],
            MaxTokens = 500
        });

        Assert.Contains(pack.ArtifactRefs, artifact => artifact.Contains("input-artifact:artifact-1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(pack.ArtifactRefs, artifact => artifact.Contains("checkout button disabled", StringComparison.OrdinalIgnoreCase));
    }
}
