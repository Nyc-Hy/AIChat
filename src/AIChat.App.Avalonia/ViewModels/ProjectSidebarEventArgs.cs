using AIChat.Domain.Projects;

namespace AIChat.App.Avalonia.ViewModels;

// Raised by ProjectSidebarViewModel whenever the user picks a different
// project (or after a project is added). The parent view-model updates
// its StatusMessage in response — it does not cache the project itself;
// reads go through ProjectSidebarViewModel.CurrentProject.
public sealed class ProjectSelectionChangedEventArgs : EventArgs
{
    public required ProjectWorkspace Project { get; init; }
    public required string StatusMessage { get; init; }
}

// Raised by ProjectSidebarViewModel when an "add project" attempt finishes,
// including the case where the folder is invalid. Lets the parent surface
// the outcome in the status line and in the activity log if desired.
public sealed class ProjectAddedEventArgs : EventArgs
{
    public ProjectWorkspace? Project { get; init; }
    public required string StatusMessage { get; init; }
    public bool Succeeded => Project is not null;
}
