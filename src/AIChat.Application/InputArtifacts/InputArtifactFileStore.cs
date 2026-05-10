using AIChat.Domain.Artifacts;

namespace AIChat.Application.Artifacts;

public sealed class InputArtifactFileStore
{
    private readonly string _rootDirectory;

    public InputArtifactFileStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AIChat", "artifacts")
            : rootDirectory;
    }

    public async Task StoreAsync(InputArtifact artifact, string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        var projectId = string.IsNullOrWhiteSpace(artifact.ProjectId) ? "project" : SanitizePathSegment(artifact.ProjectId);
        var extension = Path.GetExtension(artifact.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = Path.GetExtension(sourcePath);
        }

        var directory = Path.Combine(_rootDirectory, projectId);
        Directory.CreateDirectory(directory);
        var storedPath = Path.Combine(directory, artifact.Id + extension.ToLowerInvariant());

        await using (var source = File.OpenRead(sourcePath))
        await using (var destination = File.Create(storedPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

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

        var projectId = string.IsNullOrWhiteSpace(artifact.ProjectId) ? "project" : SanitizePathSegment(artifact.ProjectId);
        var normalizedExtension = NormalizeExtension(extension);
        var directory = Path.Combine(_rootDirectory, projectId);
        Directory.CreateDirectory(directory);
        var storedPath = Path.Combine(directory, artifact.Id + normalizedExtension);

        await File.WriteAllBytesAsync(storedPath, bytes, cancellationToken);

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

            var root = Path.GetFullPath(_rootDirectory);
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return;
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

            var root = Path.GetFullPath(_rootDirectory);
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

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
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "project" : sanitized;
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return ".bin";
        }

        return (trimmed.StartsWith('.') ? trimmed : "." + trimmed).ToLowerInvariant();
    }
}
