using AIChat.Application.Workspace;
using AIChat.App.Avalonia.ViewModels;

namespace AIChat.App.Avalonia.Composition;

// Wraps the synchronous ProjectFileIndexBuilder so the VM
// (FileTreeViewModel) can run the actual build off the UI
// thread via Task.Run without knowing about the concrete
// builder type. The real builder walks the disk and is the
// only place that should be wrapped — keeps the VM testable
// with a fake IProjectFileIndexFactory.
public sealed class ProjectFileIndexFactory : IProjectFileIndexFactory
{
    public ProjectFileIndex Build(string rootPath)
        => new ProjectFileIndexBuilder().Build(rootPath, maxFiles: 500);
}
