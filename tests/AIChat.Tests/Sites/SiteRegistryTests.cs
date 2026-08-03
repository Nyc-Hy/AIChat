using AIChat.Application.Sites;
using AIChat.Domain.Sites;

namespace AIChat.Tests.Sites;

// Wave 9 (parity plan §7 Wave 9): pin the SiteRegistry
// contract that the SitesView + preview runner will depend
// on. Mirrors ScheduledTaskRegistryTests — same shape, same
// temp-directory isolation.
public sealed class SiteRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly string _sitesFile;
    private readonly string _deploymentsFile;
    private readonly SiteRegistry _registry;

    public SiteRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "aichat-site-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _sitesFile = Path.Combine(_root, "sites.json");
        _deploymentsFile = Path.Combine(_root, "deployments.json");
        _registry = new SiteRegistry(_sitesFile, _deploymentsFile);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ReloadAsync_EmptyFiles_ProducesEmptyState()
    {
        await _registry.ReloadAsync();

        Assert.Empty(_registry.Sites);
        Assert.Empty(_registry.Deployments);
    }

    [Fact]
    public async Task AddAsync_PersistsSiteAndFiresChanged()
    {
        var fired = 0;
        _registry.Changed += (_, _) => fired++;

        var site = new Site { Name = "Marketing site", SourcePath = "/tmp/site" };
        var id = await _registry.AddAsync(site);

        Assert.Equal(site.Id, id);
        Assert.True(File.Exists(_sitesFile));
        var reloaded = new SiteRegistry(_sitesFile, _deploymentsFile);
        await reloaded.ReloadAsync();
        Assert.Single(reloaded.Sites);
        Assert.Equal("Marketing site", reloaded.Sites[0].Name);
        Assert.True(fired >= 1);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesRowById()
    {
        var site = new Site { Name = "old", Port = 3000 };
        await _registry.AddAsync(site);

        site.Port = 4000;
        var updated = await _registry.UpdateAsync(site);

        Assert.True(updated);
        Assert.Equal(4000, _registry.Sites[0].Port);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsFalse()
    {
        var updated = await _registry.UpdateAsync(new Site { Id = "missing", Name = "x" });
        Assert.False(updated);
    }

    [Fact]
    public async Task RemoveAsync_DropsSiteAndCascadesDeployments()
    {
        var site = new Site { Name = "s" };
        await _registry.AddAsync(site);
        await _registry.RecordDeploymentAsync(new SiteDeployment
        {
            SiteId = site.Id,
            Status = SiteDeploymentStatus.Succeeded,
            Url = "http://localhost:3000",
        });

        var removed = await _registry.RemoveAsync(site.Id);

        Assert.True(removed);
        Assert.Empty(_registry.Sites);
        Assert.Empty(_registry.Deployments);
    }

    [Fact]
    public async Task RecordDeploymentAsync_AppendsRowAndBumpsLastPreviewAt()
    {
        var site = new Site { Name = "s" };
        await _registry.AddAsync(site);
        Assert.Null(_registry.Sites[0].LastPreviewAt);

        var deployment = new SiteDeployment
        {
            SiteId = site.Id,
            Status = SiteDeploymentStatus.Running,
            Url = "http://localhost:3000",
        };
        await _registry.RecordDeploymentAsync(deployment);

        Assert.Single(_registry.Deployments);
        Assert.Equal(deployment.Id, _registry.Deployments[0].Id);
        Assert.NotNull(_registry.Sites[0].LastPreviewAt);
    }
}
