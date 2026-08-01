namespace AIChat.Application.Workspace;

// Immutable tree node built from a flat ProjectFileIndex. The
// Avalonia TreeView needs a hierarchical structure, but the
// existing ProjectFileIndexBuilder produces a flat list
// (Entries) so it can stay focused on the agent's "what files
// exist in this project" question. This builder is the bridge:
// it walks the flat list, splits each RelativePath on '/', and
// returns a root that can be bound directly to a TreeView.
public sealed record FileTreeNode(
    string Name,
    string RelativePath,
    bool IsFolder,
    long SizeBytes,
    string TypeTag,
    IReadOnlyList<FileTreeNode> Children)
{
    // Recursive count of file nodes in this subtree. Used for
    // the "src/ (12)" badge next to folder names so the user
    // can tell at a glance how big a directory is.
    public int FileCount
    {
        get
        {
            if (!IsFolder)
            {
                return 1;
            }
            var total = 0;
            foreach (var child in Children)
            {
                total += child.FileCount;
            }
            return total;
        }
    }
}

public static class FileTreeBuilder
{
    // Builds a tree from a flat ProjectFileIndex. Folders that
    // exist only implicitly (e.g. the file is "src/Foo/Bar.cs"
    // but no other entry lives directly under "src/Foo/" — the
    // flat index doesn't carry folder entries) are synthesized
    // on the fly. Folders with no files in them are dropped so
    // the tree stays focused on content.
    public static FileTreeNode Build(ProjectFileIndex index)
    {
        var rootChildren = new List<MutableNode>();
        foreach (var entry in index.Entries)
        {
            // ProjectFileIndexBuilder uses Path.GetRelativePath which
            // emits native separators (\ on Windows, / on Unix). The
            // tree's children are bound to a TreeView where the
            // "/" separator is the natural way to address paths, so
            // normalize up front and feed the leaf the same string.
            var normalizedPath = entry.RelativePath.Replace('\\', '/');
            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var currentChildren = rootChildren;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var segment = segments[i];
                var existing = currentChildren.FirstOrDefault(node =>
                    node.IsFolder && string.Equals(node.Name, segment, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    var folderPath = string.Join('/', segments.Take(i + 1));
                    var newFolder = new MutableNode
                    {
                        Name = segment,
                        RelativePath = folderPath,
                        IsFolder = true,
                        SizeBytes = 0,
                        TypeTag = "",
                    };
                    currentChildren.Add(newFolder);
                    currentChildren = newFolder.Children;
                }
                else
                {
                    currentChildren = existing.Children;
                }
            }
            currentChildren.Add(new MutableNode
            {
                Name = segments[^1],
                RelativePath = normalizedPath,
                IsFolder = false,
                SizeBytes = entry.SizeBytes,
                TypeTag = entry.TypeTag,
            });
        }

        return new FileTreeNode(
            Name: "<root>",
            RelativePath: "",
            IsFolder: true,
            SizeBytes: 0,
            TypeTag: "",
            Children: Freeze(rootChildren));
    }

    // Sorts folders-first then alphabetical, recurses into
    // children, and returns an immutable IReadOnlyList.
    private static IReadOnlyList<FileTreeNode> Freeze(List<MutableNode> nodes)
    {
        var sorted = nodes
            .OrderByDescending(node => node.IsFolder)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new List<FileTreeNode>(sorted.Count);
        foreach (var node in sorted)
        {
            result.Add(new FileTreeNode(
                node.Name,
                node.RelativePath,
                node.IsFolder,
                node.SizeBytes,
                node.TypeTag,
                Freeze(node.Children)));
        }
        return result;
    }

    // Mutable internal node used during construction. The
    // immutable FileTreeNode is the public shape (records with
    // IReadOnlyList children bind cleanly to Avalonia's
    // TreeView), but building a tree of immutable nodes means
    // every Add would allocate a new node. This mutable mirror
    // is the temporary holder.
    private sealed class MutableNode
    {
        public string Name { get; init; } = "";
        public string RelativePath { get; init; } = "";
        public bool IsFolder { get; init; }
        public long SizeBytes { get; init; }
        public string TypeTag { get; init; } = "";
        public List<MutableNode> Children { get; } = new();
    }
}
