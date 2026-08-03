using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Artifacts;
using AIChat.Application.Tools;
using AIChat.Domain.Projects;
using AIChat.Domain.Sources;
using AIChat.Tests.TestDoubles;
using Moq;

namespace AIChat.Tests.Sources;

// End-to-end coverage of the @-reference send path:
//   user types "@web:abc 总结" → SendTask
//     → PromoteSourceReferencesAsync
//       → SourceReferenceParser.Parse
//       → InputArtifactService.Create
//       → InputArtifactFileStore.StoreBytesAsync
//       → project.InputArtifacts.Add
//       → repository.SaveWorkspacesAsync
//
// The unit tests in SourceReferenceParserTests cover
// the parser, and InputArtifactRefSystemPromptTests
// covers the InputArtifact → system-prompt pipeline.
// This file locks the *glue* between those two — the
// thin but easy-to-break send path that promotes
// @-text into project-level artifacts and persists
// the workspace so the attachments survive an app
// restart.
public class AgentHostPromoteSourceReferencesTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        "AIChatPromoteSourceReferencesTests",
        Guid.NewGuid().ToString("N"));

    public AgentHostPromoteSourceReferencesTests()
    {
        Directory.CreateDirectory(_dataDirectory);
        Environment.SetEnvironmentVariable("AICHAT_ISOLATED_DATA_ROOT", _dataDirectory);
    }

    [Fact]
    public async Task Promote_NoReferences_IsNoOp()
    {
        var (host, _, project, _) = await CreateHostAsync();

        await host.PromoteSourceReferencesAsync("just a plain prompt", project);

        Assert.Empty(project.InputArtifacts);
    }

    [Fact]
    public async Task Promote_SingleReference_AddsArtifactAndPersists()
    {
        var (host, registry, project, repository) = await CreateHostAsync();
        await registry.AddAsync(NewSource("web", "My Article",
            "the article body"));

        await host.PromoteSourceReferencesAsync(
            "总结 @web:abc 给我", project);

        var artifact = Assert.Single(project.InputArtifacts);
        Assert.Equal("My Article", artifact.FileName);
        Assert.Equal("the article body", artifact.RawText);
        // Send-persisted to the repository (the
        // production send path also calls
        // SaveWorkspacesAsync so the artifacts
        // survive an app restart — that's the
        // property the next assertion locks).
        var repos = await repository.LoadWorkspacesAsync();
        var saved = Assert.Single(repos);
        Assert.Single(saved.InputArtifacts);
        Assert.Equal("the article body", saved.InputArtifacts[0].RawText);
    }

    [Fact]
    public async Task Promote_TwoReferences_AddsTwoArtifacts()
    {
        var (host, registry, project, _) = await CreateHostAsync();
        await registry.AddAsync(NewSource("web", "Article A", "body A"));
        await registry.AddAsync(NewSource("clipboard", "Clip B", "body B"));

        await host.PromoteSourceReferencesAsync(
            "总结 @web:abc 和 @clipboard:def 给我", project);

        Assert.Equal(2, project.InputArtifacts.Count);
        Assert.Contains(project.InputArtifacts, a => a.RawText == "body A");
        Assert.Contains(project.InputArtifacts, a => a.RawText == "body B");
    }

    [Fact]
    public async Task Promote_UnknownReference_Skipped()
    {
        var (host, registry, project, _) = await CreateHostAsync();
        await registry.AddAsync(NewSource("web", "Article A", "body A"));

        // @web:zzz doesn't exist in the registry.
        await host.PromoteSourceReferencesAsync(
            "总结 @web:zzz @web:abc 给我", project);

        var artifact = Assert.Single(project.InputArtifacts);
        Assert.Equal("body A", artifact.RawText);
    }

    [Fact]
    public async Task Promote_PreservesFileOnDisk()
    {
        var (host, registry, project, _) = await CreateHostAsync();
        await registry.AddAsync(NewSource("web", "Article A", "body A"));

        await host.PromoteSourceReferencesAsync(
            "总结 @web:abc 给我", project);

        // The artifact body should also be on disk
        // (the production path stores the bytes via
        // InputArtifactFileStore so the agent loop
        // can read the full body via
        // read_input_artifact — a daily-driver user
        // paste-attaching a large article relies on
        // this).
        var artifact = Assert.Single(project.InputArtifacts);
        var allFiles = Directory.GetFiles(_dataDirectory, "*", SearchOption.AllDirectories);
        Assert.Contains(allFiles, path => path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        // Round-trip the content.
        var file = allFiles.First(path => path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("body A", await File.ReadAllTextAsync(file));
        // And the metadata ref is stamped (the
        // SystemPromptBuilder path uses it to
        // resolve the artifact id the agent sees
        // in the "输入 artifact" section).
        Assert.True(artifact.Metadata.ContainsKey("ref"));
    }

    [Fact]
    public async Task Promote_DuplicateReference_AddsOnce()
    {
        // Same source referenced twice in the same
        // prompt (the user pasted the article +
        // clicked 引用 + retyped it). The parser
        // dedupes by id, so only one artifact
        // should land.
        var (host, registry, project, _) = await CreateHostAsync();
        await registry.AddAsync(NewSource("web", "Article A", "body A"));

        await host.PromoteSourceReferencesAsync(
            "总结 @web:abc 和 @web:abc", project);

        Assert.Single(project.InputArtifacts);
    }

    private static Source NewSource(
        string kind, string displayName, string content) => new()
    {
        Id = kind == "web" ? "abc" : "def",
        Kind = kind,
        DisplayName = displayName,
        Content = content,
    };

    private async Task<(AgentHostViewModel Host, InMemorySourceRegistry Registry, WorkspaceProject Project, InMemoryAppRepository Repository)>
        CreateHostAsync()
    {
        var repository = new InMemoryAppRepository();
        var settingsHolder = new SettingsHolder();
        settingsHolder.Replace(new AppSettings());
        var sidebar = new ProjectSidebarViewModel(repository, settingsHolder);
        var activity = new ActivityFeedViewModel();
        var toast = new ToastService(action => action());
        var registry = new InMemorySourceRegistry();
        var artifactFileStore = new InputArtifactFileStore(_dataDirectory);
        var host = new AgentHostViewModel(
            Mock.Of<IChatCompletionService>(),
            AgentToolRegistry.CreateForTests([]),
            Mock.Of<IApprovalService>(),
            repository,
            sidebar,
            new ConversationListViewModel(repository),
            activity,
            toast,
            registry,
            _ => { },
            () => settingsHolder.Current,
            () => false,
            () => false,
            action =>
            {
                action();
                return Task.CompletedTask;
            },
            artifactFileStore);
        var project = new WorkspaceProject
        {
            Id = "project-1",
            Name = "Test",
            Folders = [new WorkspaceFolder { Id = "primary-1", Path = _dataDirectory }],
            PrimaryFolderId = "primary-1",
        };
        await repository.SaveWorkspacesAsync([project]);
        // Sidebar needs to know about the project
        // so CurrentProject wires up.
        sidebar.Refresh([project]);
        return (host, registry, project, repository);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AICHAT_ISOLATED_DATA_ROOT", null);
        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch
        {
        }
    }
}
