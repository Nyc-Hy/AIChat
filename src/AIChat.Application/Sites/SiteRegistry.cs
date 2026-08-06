using AIChat.Abstractions.Configuration;
using AIChat.Application.Persistence;
using AIChat.Domain.Sites;

namespace AIChat.Application.Sites;

// Concrete ISiteRegistry. Same shape and threading
// contract as ScheduledTaskRegistry; see the comment on
// JsonFileStore for the atomic-write story. Tests can
// pass custom file paths via the ctor.
public sealed class SiteRegistry : ISiteRegistry
{
    private readonly string _sitesFilePath;
    private readonly string _deploymentsFilePath;
    private readonly object _gate = new();

    private List<Site> _sites = [];
    private List<SiteDeployment> _deployments = [];

    public SiteRegistry(string? sitesFilePath = null, string? deploymentsFilePath = null)
    {
        _sitesFilePath = sitesFilePath ?? AppRuntimeProfile.SitesFile;
        _deploymentsFilePath = deploymentsFilePath ?? AppRuntimeProfile.SiteDeploymentsFile;
    }

    public IReadOnlyList<Site> Sites
    {
        get { lock (_gate) { return _sites.ToArray(); } }
    }

    public IReadOnlyList<SiteDeployment> Deployments
    {
        get { lock (_gate) { return _deployments.ToArray(); } }
    }

    public event EventHandler? Changed;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var sites = await JsonFileStore
            .LoadListAsync<Site>(_sitesFilePath, cancellationToken)
            .ConfigureAwait(false);
        var deployments = await JsonFileStore
            .LoadListAsync<SiteDeployment>(_deploymentsFilePath, cancellationToken)
            .ConfigureAwait(false);

        lock (_gate)
        {
            _sites = sites;
            _deployments = deployments;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<string> AddAsync(Site site, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(site.Id))
        {
            site.Id = Guid.NewGuid().ToString("N");
        }
        if (site.CreatedAt == default)
        {
            site.CreatedAt = DateTimeOffset.UtcNow;
        }

        List<Site> snapshot;
        lock (_gate)
        {
            _sites.Add(site);
            snapshot = _sites.ToList();
        }

        await JsonFileStore.SaveListAsync(_sitesFilePath, snapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return site.Id;
    }

    public async Task<bool> UpdateAsync(Site site, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(site.Id))
        {
            return false;
        }

        List<Site> snapshot;
        lock (_gate)
        {
            var index = _sites.FindIndex(existing => existing.Id == site.Id);
            if (index < 0)
            {
                return false;
            }
            _sites[index] = site;
            snapshot = _sites.ToList();
        }

        await JsonFileStore.SaveListAsync(_sitesFilePath, snapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> RemoveAsync(string siteId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(siteId))
        {
            return false;
        }

        List<Site> sitesSnapshot;
        List<SiteDeployment> deploymentsSnapshot;
        lock (_gate)
        {
            var index = _sites.FindIndex(existing => existing.Id == siteId);
            if (index < 0)
            {
                return false;
            }
            _sites.RemoveAt(index);
            _deployments.RemoveAll(d => d.SiteId == siteId);
            sitesSnapshot = _sites.ToList();
            deploymentsSnapshot = _deployments.ToList();
        }

        await JsonFileStore.SaveListAsync(_sitesFilePath, sitesSnapshot, cancellationToken)
            .ConfigureAwait(false);
        await JsonFileStore.SaveListAsync(_deploymentsFilePath, deploymentsSnapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<string> RecordDeploymentAsync(SiteDeployment deployment, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deployment.Id))
        {
            deployment.Id = Guid.NewGuid().ToString("N");
        }
        if (deployment.StartedAt == default)
        {
            deployment.StartedAt = DateTimeOffset.UtcNow;
        }

        List<SiteDeployment> deploymentsSnapshot;
        List<Site> sitesSnapshot;
        lock (_gate)
        {
            _deployments.Add(deployment);
            // Bump LastPreviewAt on the parent site so the
            // list shows "last previewed 5 min ago" without
            // a separate refresh. The local preview runner
            // (follow-up slice) writes the real URL.
            var siteIndex = _sites.FindIndex(existing => existing.Id == deployment.SiteId);
            if (siteIndex >= 0)
            {
                _sites[siteIndex].LastPreviewAt = deployment.StartedAt;
            }
            deploymentsSnapshot = _deployments.ToList();
            sitesSnapshot = _sites.ToList();
        }

        await JsonFileStore.SaveListAsync(_deploymentsFilePath, deploymentsSnapshot, cancellationToken)
            .ConfigureAwait(false);
        await JsonFileStore.SaveListAsync(_sitesFilePath, sitesSnapshot, cancellationToken)
            .ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
        return deployment.Id;
    }
}
