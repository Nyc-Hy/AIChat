using AIChat.Abstractions.Configuration;
using AIChat.Domain.Projects;

namespace AIChat.Abstractions.Persistence;

// Persistence boundary. The app currently stores JSON locally, but the ViewModel
// only depends on this interface, so storage can be changed without touching UI.
public interface IAppRepository
{
    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectWorkspace>> LoadProjectsAsync(CancellationToken cancellationToken = default);
    Task SaveProjectsAsync(IReadOnlyList<ProjectWorkspace> projects, CancellationToken cancellationToken = default);
}
