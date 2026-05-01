using AIChat.Application.Workspace;
using AIChat.Tests.Tools;

namespace AIChat.Tests.Workspace;

public sealed class ProjectFileIndexBuilderTests : IDisposable
{
    private readonly TemporaryWorkspace _workspace = TemporaryWorkspace.Create();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Build_ReturnsEmptyIndexForNonExistentPath()
    {
        var index = new ProjectFileIndexBuilder().Build("/nonexistent/path");
        Assert.Empty(index.Entries);
    }

    [Fact]
    public void Build_IndexesSourceFiles()
    {
        File.WriteAllText(Path.Combine(_workspace.Path, "Program.cs"), "class P {}");
        File.WriteAllText(Path.Combine(_workspace.Path, "App.xaml"), "<Window/>");

        var index = new ProjectFileIndexBuilder().Build(_workspace.Path);

        Assert.Equal(2, index.Entries.Count);
        Assert.All(index.Entries, e => Assert.Equal("source", e.TypeTag));
    }

    [Fact]
    public void Build_SkipsIgnoredDirectories()
    {
        var binDir = Path.Combine(_workspace.Path, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "output.dll"), "binary");
        File.WriteAllText(Path.Combine(_workspace.Path, "App.cs"), "class A {}");

        var index = new ProjectFileIndexBuilder().Build(_workspace.Path);

        Assert.Single(index.Entries);
        Assert.Equal("App.cs", Path.GetFileName(index.Entries[0].RelativePath));
    }

    [Fact]
    public void Build_SkipsBinaryExtensions()
    {
        File.WriteAllText(Path.Combine(_workspace.Path, "App.cs"), "class A {}");
        File.WriteAllText(Path.Combine(_workspace.Path, "icon.png"), "binary");
        File.WriteAllText(Path.Combine(_workspace.Path, "lib.dll"), "binary");

        var index = new ProjectFileIndexBuilder().Build(_workspace.Path);

        Assert.Single(index.Entries);
        Assert.Equal(".cs", index.Entries[0].Extension);
    }

    [Fact]
    public void Build_ClassifiesConfigFiles()
    {
        File.WriteAllText(Path.Combine(_workspace.Path, "app.json"), "{}");
        File.WriteAllText(Path.Combine(_workspace.Path, "App.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_workspace.Path, "AIChat.sln"), "solution");

        var index = new ProjectFileIndexBuilder().Build(_workspace.Path);

        Assert.All(index.Entries, e => Assert.Equal("config", e.TypeTag));
    }

    [Fact]
    public void Build_ClassifiesDocFiles()
    {
        File.WriteAllText(Path.Combine(_workspace.Path, "README.md"), "# Hello");
        File.WriteAllText(Path.Combine(_workspace.Path, "notes.txt"), "notes");

        var index = new ProjectFileIndexBuilder().Build(_workspace.Path);

        Assert.All(index.Entries, e => Assert.Equal("doc", e.TypeTag));
    }

    [Fact]
    public void Build_ClassifiesTestFiles()
    {
        var testDir = Path.Combine(_workspace.Path, "tests", "MyTests");
        Directory.CreateDirectory(testDir);
        File.WriteAllText(Path.Combine(testDir, "UnitTest1.cs"), "class T {}");
        File.WriteAllText(Path.Combine(_workspace.Path, "AppTest.cs"), "class T {}");

        var index = new ProjectFileIndexBuilder().Build(_workspace.Path);

        Assert.All(index.Entries, e => Assert.Equal("test", e.TypeTag));
    }

    [Fact]
    public void Build_TruncatesWhenMaxFilesExceeded()
    {
        for (var i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(_workspace.Path, $"File{i}.cs"), "class C {}");
        }

        var index = new ProjectFileIndexBuilder().Build(_workspace.Path, maxFiles: 5);

        Assert.Equal(5, index.Entries.Count);
        Assert.True(index.WasTruncated);
    }

    [Fact]
    public void Build_RecordsFileSize()
    {
        File.WriteAllText(Path.Combine(_workspace.Path, "App.cs"), "class A {}");

        var index = new ProjectFileIndexBuilder().Build(_workspace.Path);

        Assert.Single(index.Entries);
        Assert.True(index.Entries[0].SizeBytes > 0);
    }

    [Fact]
    public void ShouldIgnore_IgnoresGitAndBinDirectories()
    {
        Assert.True(ProjectFileIndexBuilder.ShouldIgnore(".git/config"));
        Assert.True(ProjectFileIndexBuilder.ShouldIgnore("src/bin/Debug/net8.0/app.dll"));
        Assert.True(ProjectFileIndexBuilder.ShouldIgnore("src/obj/project.assets.json"));
        Assert.True(ProjectFileIndexBuilder.ShouldIgnore("node_modules/package/index.js"));
        Assert.False(ProjectFileIndexBuilder.ShouldIgnore("src/App/Program.cs"));
    }

    [Fact]
    public void ClassifyFile_CorrectlyClassifiesByExtension()
    {
        Assert.Equal("source", ProjectFileIndexBuilder.ClassifyFile("App.cs", ".cs"));
        Assert.Equal("source", ProjectFileIndexBuilder.ClassifyFile("index.ts", ".ts"));
        Assert.Equal("config", ProjectFileIndexBuilder.ClassifyFile("app.json", ".json"));
        Assert.Equal("config", ProjectFileIndexBuilder.ClassifyFile("App.csproj", ".csproj"));
        Assert.Equal("doc", ProjectFileIndexBuilder.ClassifyFile("README.md", ".md"));
        Assert.Equal("asset", ProjectFileIndexBuilder.ClassifyFile("image.svg", ".svg"));
    }

    [Fact]
    public void ClassifyFile_DetectsTestDirectories()
    {
        Assert.Equal("test", ProjectFileIndexBuilder.ClassifyFile("tests/AIChat.Tests/App.cs", ".cs"));
        Assert.Equal("test", ProjectFileIndexBuilder.ClassifyFile("test/unit/helper.cs", ".cs"));
    }

    [Fact]
    public void ClassifyFile_DetectsTestFileNames()
    {
        Assert.Equal("test", ProjectFileIndexBuilder.ClassifyFile("AppTest.cs", ".cs"));
        Assert.Equal("test", ProjectFileIndexBuilder.ClassifyFile("AppTests.cs", ".cs"));
        Assert.Equal("test", ProjectFileIndexBuilder.ClassifyFile("AppSpec.ts", ".ts"));
    }
}
