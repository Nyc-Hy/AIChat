using AIChat.Abstractions.Configuration;
using AIChat.Domain.Artifacts;

namespace AIChat.Application.Artifacts;

public sealed class InputArtifactFileStore
{
    private readonly string _rootDirectory;

    public InputArtifactFileStore(string? rootDirectory = null)
    {
        _rootDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(rootDirectory)
            ? AppRuntimeProfile.ArtifactsDirectory
            : rootDirectory);
    }

    public async Task StoreAsync(InputArtifact artifact, string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        var extension = Path.GetExtension(artifact.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = Path.GetExtension(sourcePath);
        }

        var storedPath = CreateManagedFilePath(artifact, NormalizeExtension(extension));

        await WriteAtomicallyAsync(
            storedPath,
            async destination =>
            {
                await using var source = File.OpenRead(sourcePath);
                await source.CopyToAsync(destination, cancellationToken);
            },
            cancellationToken);

        artifact.Metadata["storedPath"] = storedPath;
        artifact.Metadata["storedRelativePath"] = Path.GetRelativePath(_rootDirectory, storedPath);
    }

    public async Task StoreBytesAsync(
        InputArtifact artifact,
        byte[] bytes,
        string extension,
        CancellationToken cancellationToken = default)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        var normalizedExtension = NormalizeExtension(extension);
        var storedPath = CreateManagedFilePath(artifact, normalizedExtension);

        await WriteAtomicallyAsync(
            storedPath,
            destination => destination.WriteAsync(bytes, cancellationToken).AsTask(),
            cancellationToken);

        artifact.Metadata["storedPath"] = storedPath;
        artifact.Metadata["storedRelativePath"] = Path.GetRelativePath(_rootDirectory, storedPath);
    }

    public void DeleteStoredFile(InputArtifact artifact)
    {
        if (!artifact.Metadata.TryGetValue("storedPath", out var storedPath))
        {
            return;
        }

        TryDeleteManagedPath(storedPath);
    }

    public void DeleteStoredFiles(IEnumerable<InputArtifact> artifacts)
    {
        foreach (var artifact in artifacts)
        {
            DeleteStoredFile(artifact);
        }
    }

    public void DeleteProjectStore(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        TryDeleteManagedDirectory(Path.Combine(_rootDirectory, SanitizePathSegment(projectId)));
    }

    private void TryDeleteManagedPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            EnsureInsideRoot(fullPath, allowRoot: false);
            EnsureNotSymbolicLink(_rootDirectory);
            if (Path.GetDirectoryName(fullPath) is { } parent)
            {
                EnsureNotSymbolicLink(parent);
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // Managed artifact file cleanup is best-effort; project metadata stays authoritative.
        }
    }

    private void TryDeleteManagedDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            EnsureInsideRoot(fullPath, allowRoot: false);
            EnsureNotSymbolicLink(_rootDirectory);
            EnsureNotSymbolicLink(fullPath);

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch
        {
            // Managed artifact file cleanup is best-effort; project metadata stays authoritative.
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var chars = value
            .Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_')
            .ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) || sanitized is "." or ".."
            ? "project"
            : sanitized;
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return ".bin";
        }

        var normalized = (trimmed.StartsWith('.') ? trimmed : "." + trimmed).ToLowerInvariant();
        if (normalized.Length > 16 ||
            normalized.Length < 2 ||
            normalized.Skip(1).Any(ch => !char.IsAsciiLetterOrDigit(ch)))
        {
            throw new ArgumentException("文件扩展名包含不安全字符。", nameof(extension));
        }

        return normalized;
    }

    private string CreateManagedFilePath(InputArtifact artifact, string extension)
    {
        ValidateArtifactId(artifact.Id);
        var projectId = string.IsNullOrWhiteSpace(artifact.ProjectId)
            ? "project"
            : SanitizePathSegment(artifact.ProjectId);
        var directory = Path.GetFullPath(Path.Combine(_rootDirectory, projectId));
        EnsureInsideRoot(directory, allowRoot: false);

        Directory.CreateDirectory(_rootDirectory);
        EnsureNotSymbolicLink(_rootDirectory);
        EnsurePrivateDirectoryPermissions(_rootDirectory);
        Directory.CreateDirectory(directory);
        EnsureNotSymbolicLink(directory);
        EnsurePrivateDirectoryPermissions(directory);

        var storedPath = Path.GetFullPath(Path.Combine(directory, artifact.Id + extension));
        EnsureInsideRoot(storedPath, allowRoot: false);
        EnsureDestinationNotSymbolicLink(storedPath);
        return storedPath;
    }

    private static async Task WriteAtomicallyAsync(
        string storedPath,
        Func<FileStream, Task> writeAsync,
        CancellationToken cancellationToken)
    {
        var tempPath = $"{storedPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var destination = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            EnsurePrivateFilePermissions(tempPath);
            await writeAsync(destination);
            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);
            destination.Close();

            EnsureDestinationNotSymbolicLink(storedPath);
            File.Move(tempPath, storedPath, overwrite: true);
            EnsurePrivateFilePermissions(storedPath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup must not hide the original write failure.
            }
        }
    }

    private static void ValidateArtifactId(string artifactId)
    {
        if (string.IsNullOrWhiteSpace(artifactId) ||
            artifactId is "." or ".." ||
            artifactId.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("Artifact ID 必须是安全的单一路径片段。", nameof(artifactId));
        }
    }

    private void EnsureInsideRoot(string path, bool allowRoot)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var root = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if ((allowRoot && string.Equals(fullPath, root, comparison)) ||
            fullPath.StartsWith(root + Path.DirectorySeparatorChar, comparison))
        {
            return;
        }

        throw new InvalidOperationException("Artifact 路径超出托管目录。");
    }

    private static void EnsureNotSymbolicLink(string path)
    {
        var info = new DirectoryInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("Artifact 托管目录不能是符号链接。");
        }
    }

    private static void EnsureDestinationNotSymbolicLink(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null ||
            (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)))
        {
            throw new InvalidOperationException("Artifact 托管文件不能是符号链接。");
        }
    }

    private static void EnsurePrivateDirectoryPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void EnsurePrivateFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
