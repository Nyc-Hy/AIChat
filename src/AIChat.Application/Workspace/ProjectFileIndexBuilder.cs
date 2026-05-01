namespace AIChat.Application.Workspace;

public sealed class ProjectFileIndexBuilder
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "artifacts", "TestResults", "node_modules"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".bmp",
        ".zip", ".7z", ".tar", ".gz", ".rar", ".pdf", ".woff", ".woff2", ".ttf",
        ".eot", ".mp3", ".mp4", ".avi", ".mov", ".wav", ".sqlite", ".db"
    };

    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".java", ".go", ".rs",
        ".cpp", ".c", ".h", ".hpp", ".swift", ".kt", ".rb", ".php", ".lua",
        ".xaml", ".vue", ".svelte", ".css", ".scss", ".less", ".html", ".razor"
    };

    private static readonly HashSet<string> ConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".env", ".csproj",
        ".fsproj", ".vbproj", ".sln", ".slnx", ".props", ".targets", ".config",
        ".editorconfig", ".gitignore", ".gitattributes", ".dockerignore",
        ".prettierrc", ".eslintrc"
    };

    private static readonly HashSet<string> DocExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".rst", ".adoc", ".wiki"
    };

    public ProjectFileIndex Build(string rootPath, int maxFiles = 500)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return new ProjectFileIndex { RootPath = rootPath };
        }

        var entries = new List<ProjectFileIndexEntry>();
        var wasTruncated = false;

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                if (entries.Count >= maxFiles)
                {
                    wasTruncated = true;
                    break;
                }

                var relativePath = GetRelativePath(rootPath, filePath);
                if (ShouldIgnore(relativePath))
                {
                    continue;
                }

                var extension = Path.GetExtension(filePath);
                if (BinaryExtensions.Contains(extension))
                {
                    continue;
                }

                long size = 0;
                try
                {
                    size = new FileInfo(filePath).Length;
                }
                catch
                {
                    // Skip files we can't stat
                    continue;
                }

                entries.Add(new ProjectFileIndexEntry
                {
                    RelativePath = relativePath,
                    SizeBytes = size,
                    Extension = extension,
                    TypeTag = ClassifyFile(relativePath, extension)
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we can't access
        }
        catch (IOException)
        {
            // Skip I/O errors
        }

        return new ProjectFileIndex
        {
            RootPath = rootPath,
            Entries = entries.OrderBy(e => e.TypeTag).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
            GeneratedAt = DateTimeOffset.Now,
            WasTruncated = wasTruncated
        };
    }

    public static bool ShouldIgnore(string relativePath)
    {
        var segments = relativePath.Replace('\\', '/').Split('/');
        foreach (var segment in segments)
        {
            if (IgnoredDirectories.Contains(segment))
            {
                return true;
            }
        }
        return false;
    }

    public static string ClassifyFile(string relativePath, string extension)
    {
        var normalized = relativePath.Replace('\\', '/');
        var pathSegments = normalized.Split('/');

        // Check if file is in a test directory
        foreach (var segment in pathSegments)
        {
            if (segment.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
                (segment.StartsWith("test", StringComparison.OrdinalIgnoreCase) &&
                 segment.Length > 4 && !char.IsLetter(segment[4])))
            {
                return "test";
            }
        }

        // Check if filename suggests test
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (fileName.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Spec", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Specs", StringComparison.OrdinalIgnoreCase))
        {
            return "test";
        }

        if (SourceExtensions.Contains(extension))
        {
            return "source";
        }

        if (ConfigExtensions.Contains(extension))
        {
            return "config";
        }

        if (DocExtensions.Contains(extension))
        {
            return "doc";
        }

        return "asset";
    }

    private static string GetRelativePath(string rootPath, string fullPath)
    {
        if (!rootPath.EndsWith(Path.DirectorySeparatorChar) && !rootPath.EndsWith(Path.AltDirectorySeparatorChar))
        {
            rootPath += Path.DirectorySeparatorChar;
        }

        if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath[rootPath.Length..];
        }

        return fullPath;
    }
}
