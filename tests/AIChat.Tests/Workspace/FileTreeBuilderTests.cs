using AIChat.Application.Workspace;

namespace AIChat.Tests.Workspace;

// Unit tests for the flat-list → tree bridge. The tree builder
// has to handle implicit folders (a file at "src/Foo/Bar.cs"
// implies a "src/Foo" folder even if no other file lives there),
// preserve file metadata, and produce a stable sort order. Each
// test below covers one of those contracts.
public class FileTreeBuilderTests
{
    [Fact]
    public void Build_EmptyIndex_ReturnsEmptyRoot()
    {
        var tree = FileTreeBuilder.Build(new ProjectFileIndex { RootPath = "/tmp/x" });

        Assert.True(tree.IsFolder);
        Assert.Empty(tree.Children);
    }

    [Fact]
    public void Build_SingleFile_ProducesSingleLeaf()
    {
        var entries = new List<ProjectFileIndexEntry>
        {
            new() { RelativePath = "README.md", SizeBytes = 100, Extension = ".md", TypeTag = "doc" }
        };
        var tree = FileTreeBuilder.Build(new ProjectFileIndex
        {
            RootPath = "/tmp/x",
            Entries = entries
        });

        var child = Assert.Single(tree.Children);
        Assert.False(child.IsFolder);
        Assert.Equal("README.md", child.Name);
        Assert.Equal("README.md", child.RelativePath);
        Assert.Equal(100, child.SizeBytes);
    }

    [Fact]
    public void Build_NestedFile_SynthesizesImplicitFolders()
    {
        // No "src/" or "src/Foo/" entries in the flat list — just
        // the leaf. The tree has to invent the folder chain on the
        // fly or the leaf would have no parent to live under.
        var entries = new List<ProjectFileIndexEntry>
        {
            new() { RelativePath = "src/Foo/Bar.cs", SizeBytes = 1_000, Extension = ".cs", TypeTag = "source" }
        };
        var tree = FileTreeBuilder.Build(new ProjectFileIndex
        {
            RootPath = "/tmp/x",
            Entries = entries
        });

        var src = Assert.Single(tree.Children);
        Assert.True(src.IsFolder);
        Assert.Equal("src", src.Name);

        var foo = Assert.Single(src.Children);
        Assert.True(foo.IsFolder);
        Assert.Equal("Foo", foo.Name);

        var bar = Assert.Single(foo.Children);
        Assert.False(bar.IsFolder);
        Assert.Equal("Bar.cs", bar.Name);
        Assert.Equal("src/Foo/Bar.cs", bar.RelativePath);
    }

    [Fact]
    public void Build_MultipleSiblings_GroupedUnderSameFolder()
    {
        var entries = new List<ProjectFileIndexEntry>
        {
            new() { RelativePath = "src/A.cs", SizeBytes = 100, Extension = ".cs", TypeTag = "source" },
            new() { RelativePath = "src/B.cs", SizeBytes = 200, Extension = ".cs", TypeTag = "source" },
            new() { RelativePath = "src/Sub/C.cs", SizeBytes = 300, Extension = ".cs", TypeTag = "source" }
        };
        var tree = FileTreeBuilder.Build(new ProjectFileIndex
        {
            RootPath = "/tmp/x",
            Entries = entries
        });

        var src = Assert.Single(tree.Children);
        Assert.True(src.IsFolder);
        // src has 3 files: A, B, Sub/C → src.FileCount should be 3.
        Assert.Equal(3, src.FileCount);

        // Folders-first then alphabetical. Sub is a folder, so it
        // comes before A and B which are files.
        Assert.Equal(3, src.Children.Count);
        Assert.True(src.Children[0].IsFolder);
        Assert.Equal("Sub", src.Children[0].Name);
        Assert.Equal("A.cs", src.Children[1].Name);
        Assert.Equal("B.cs", src.Children[2].Name);
    }

    [Fact]
    public void Build_FoldersSortedBeforeFilesThenAlphabetical()
    {
        var entries = new List<ProjectFileIndexEntry>
        {
            new() { RelativePath = "zebra.md", SizeBytes = 1, Extension = ".md", TypeTag = "doc" },
            new() { RelativePath = "Apple/seed.cs", SizeBytes = 1, Extension = ".cs", TypeTag = "source" },
            new() { RelativePath = "banana.txt", SizeBytes = 1, Extension = ".txt", TypeTag = "doc" }
        };
        var tree = FileTreeBuilder.Build(new ProjectFileIndex
        {
            RootPath = "/tmp/x",
            Entries = entries
        });

        // Expected: [Apple/, banana.txt, zebra.md] — folder first,
        // then files alphabetical (case-insensitive).
        Assert.Equal(3, tree.Children.Count);
        Assert.True(tree.Children[0].IsFolder);
        Assert.Equal("Apple", tree.Children[0].Name);
        Assert.Equal("banana.txt", tree.Children[1].Name);
        Assert.Equal("zebra.md", tree.Children[2].Name);
    }

    [Fact]
    public void Build_BackslashSeparators_NormalizedToForwardSlash()
    {
        // ProjectFileIndexBuilder uses Path.GetRelativePath which
        // can produce either separator depending on OS; the tree
        // builder has to normalize.
        var entries = new List<ProjectFileIndexEntry>
        {
            new() { RelativePath = "src\\Foo\\Bar.cs", SizeBytes = 1, Extension = ".cs", TypeTag = "source" }
        };
        var tree = FileTreeBuilder.Build(new ProjectFileIndex
        {
            RootPath = "/tmp/x",
            Entries = entries
        });

        var src = Assert.Single(tree.Children);
        Assert.Equal("src", src.Name);
        var foo = Assert.Single(src.Children);
        Assert.Equal("Foo", foo.Name);
        var bar = Assert.Single(foo.Children);
        Assert.Equal("src/Foo/Bar.cs", bar.RelativePath);
    }
}
