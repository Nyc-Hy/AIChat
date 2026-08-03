using AIChat.Domain.Sites;

namespace AIChat.Application.Sites;

// Wave 9 (parity plan §7 Wave 9): the registry surface
// the host (DI / SitesView / preview runner) needs from
// the Sites system.
//
// First slice: same shape as IScheduledTaskRegistry —
// load + persist + add / update / remove + record
// deployment. The local preview runner (a future slice)
// calls RecordDeploymentAsync when a preview starts /
// finishes; cloud deploy lands when an adapter is added.
public interface ISiteRegistry
{
    IReadOnlyList<Site> Sites { get; }
    IReadOnlyList<SiteDeployment> Deployments { get; }

    event EventHandler? Changed;

    Task ReloadAsync(CancellationToken cancellationToken = default);

    Task<string> AddAsync(Site site, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Site site, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string siteId, CancellationToken cancellationToken = default);

    // Append a deployment row + flip site.LastPreviewAt
    // (matches the ScheduledTaskRegistry pattern). The
    // runner writes the actual URL it allocated here so
    // the user can find the preview across restarts.
    Task<string> RecordDeploymentAsync(SiteDeployment deployment, CancellationToken cancellationToken = default);
}
