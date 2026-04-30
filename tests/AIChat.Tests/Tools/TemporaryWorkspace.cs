namespace AIChat.Tests.Tools;

internal sealed class TemporaryWorkspace : IDisposable
{
    private TemporaryWorkspace(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryWorkspace Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"AIChat.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TemporaryWorkspace(path);
    }

    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (var directory in Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directory, FileAttributes.Normal);
        }

        Directory.Delete(Path, recursive: true);
    }
}
