using AIChat.Application.Context;
using AIChat.Application.Workspace;
using AIChat.Domain.Context;

namespace AIChat.Tests.Context;

public sealed class ProjectContextPackBuilderTests
{
    private readonly ProjectContextPackBuilder _builder = new();

    [Fact]
    public void Build_ReturnsEmptyForNullIndexEmptySummaryNoPinned()
    {
        var result = _builder.Build(null, "", []);
        Assert.Equal("", result);
    }

    [Fact]
    public void Build_IncludesFileIndexWithGroups()
    {
        var index = new ProjectFileIndex
        {
            RootPath = "/project",
            Entries =
            [
                new ProjectFileIndexEntry { RelativePath = "App.cs", TypeTag = "source", Extension = ".cs" },
                new ProjectFileIndexEntry { RelativePath = "App.json", TypeTag = "config", Extension = ".json" }
            ]
        };

        var result = _builder.Build(index, "", []);

        Assert.Contains("## 项目文件索引", result);
        Assert.Contains("根目录：/project", result);
        Assert.Contains("文件总数：2", result);
        Assert.Contains("### 源代码 (1)", result);
        Assert.Contains("- App.cs", result);
        Assert.Contains("### 配置 (1)", result);
        Assert.Contains("- App.json", result);
    }

    [Fact]
    public void Build_IncludesWorkspaceSummary()
    {
        var result = _builder.Build(null, "分支：main，未提交变更：3 个文件", []);

        Assert.Contains("## 工作区状态", result);
        Assert.Contains("分支：main，未提交变更：3 个文件", result);
    }

    [Fact]
    public void Build_IncludesPinnedContextItems()
    {
        var items = new List<PinnedContextItem>
        {
            new() { Path = "src/App.cs", StartLine = 10, EndLine = 20, Note = "关键逻辑" },
            new() { Path = "README.md" }
        };

        var result = _builder.Build(null, "", items);

        Assert.Contains("## 固定上下文", result);
        Assert.Contains("- src/App.cs (行 10-20) — 关键逻辑", result);
        Assert.Contains("- README.md", result);
    }

    [Fact]
    public void Build_CombinesAllSections()
    {
        var index = new ProjectFileIndex
        {
            RootPath = "/project",
            Entries =
            [
                new ProjectFileIndexEntry { RelativePath = "App.cs", TypeTag = "source", Extension = ".cs" }
            ]
        };
        var items = new List<PinnedContextItem>
        {
            new() { Path = "App.cs", Note = "入口" }
        };

        var result = _builder.Build(index, "分支：dev", items);

        Assert.Contains("## 项目文件索引", result);
        Assert.Contains("## 工作区状态", result);
        Assert.Contains("## 固定上下文", result);
    }

    [Fact]
    public void Build_TrimsAssetGroupFirstWhenOverBudget()
    {
        // Create enough entries to exceed the 2000-token budget (~7200 chars)
        var entries = new List<ProjectFileIndexEntry>();
        for (var i = 0; i < 200; i++)
        {
            entries.Add(new ProjectFileIndexEntry { RelativePath = $"assets/design/mockup{i}.svg", TypeTag = "asset", Extension = ".svg" });
        }
        for (var i = 0; i < 200; i++)
        {
            entries.Add(new ProjectFileIndexEntry { RelativePath = $"src/Services/VeryLongNamespaceName/ServiceImpl{i}.cs", TypeTag = "source", Extension = ".cs" });
        }

        var index = new ProjectFileIndex
        {
            RootPath = "/project",
            Entries = entries
        };

        var result = _builder.Build(index, "", []);

        // Asset group (其他) should be trimmed, source group should remain
        Assert.DoesNotContain("### 其他", result);
        Assert.Contains("### 源代码", result);
    }

    [Fact]
    public void Build_IncludesExtensionStatistics()
    {
        var index = new ProjectFileIndex
        {
            RootPath = "/project",
            Entries =
            [
                new ProjectFileIndexEntry { RelativePath = "App.cs", TypeTag = "source", Extension = ".cs" },
                new ProjectFileIndexEntry { RelativePath = "Lib.cs", TypeTag = "source", Extension = ".cs" },
                new ProjectFileIndexEntry { RelativePath = "Helper.cs", TypeTag = "source", Extension = ".cs" },
                new ProjectFileIndexEntry { RelativePath = "app.json", TypeTag = "config", Extension = ".json" },
                new ProjectFileIndexEntry { RelativePath = "README.md", TypeTag = "doc", Extension = ".md" }
            ]
        };

        var result = _builder.Build(index, "", []);

        Assert.Contains("类型分布：.cs: 3, .json: 1, .md: 1", result);
    }

    [Fact]
    public void Build_ExtensionStatsSortedByCountDescending()
    {
        var index = new ProjectFileIndex
        {
            RootPath = "/project",
            Entries =
            [
                new ProjectFileIndexEntry { RelativePath = "a.md", TypeTag = "doc", Extension = ".md" },
                new ProjectFileIndexEntry { RelativePath = "b.md", TypeTag = "doc", Extension = ".md" },
                new ProjectFileIndexEntry { RelativePath = "App.cs", TypeTag = "source", Extension = ".cs" },
                new ProjectFileIndexEntry { RelativePath = "App.json", TypeTag = "config", Extension = ".json" },
                new ProjectFileIndexEntry { RelativePath = "Lib.json", TypeTag = "config", Extension = ".json" },
                new ProjectFileIndexEntry { RelativePath = "Helper.json", TypeTag = "config", Extension = ".json" }
            ]
        };

        var result = _builder.Build(index, "", []);

        // .json (3) should come before .md (2) which should come before .cs (1)
        var statsLine = result.Split('\n').First(l => l.StartsWith("类型分布"));
        Assert.Contains(".json: 3, .md: 2, .cs: 1", statsLine);
    }

    [Fact]
    public void Build_TruncatesWhenStillOverBudget()
    {
        // Create a massive index that exceeds even after group trimming
        var entries = new List<ProjectFileIndexEntry>();
        for (var i = 0; i < 500; i++)
        {
            entries.Add(new ProjectFileIndexEntry
            {
                RelativePath = $"src/VeryLongDirectoryName/AnotherSubDirectory/FileName{i}.cs",
                TypeTag = "source",
                Extension = ".cs"
            });
        }

        var index = new ProjectFileIndex
        {
            RootPath = "/project",
            Entries = entries
        };

        var result = _builder.Build(index, "", []);

        // Should be truncated with "..." marker
        Assert.Contains("...", result);
    }
}
