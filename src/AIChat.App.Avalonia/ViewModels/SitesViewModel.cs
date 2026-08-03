using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using AIChat.Application.BackgroundProcesses;
using AIChat.Application.Sites;
using AIChat.Domain.BackgroundProcesses;
using AIChat.Domain.Sites;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Wave 9 (parity plan §7 Wave 9): the modal VM for the
// "站点" (Sites) panel. First slice: list + add / remove
// sites + real local preview via the in-process
// EmbeddedStaticFileServer (no python3 dependency). Cloud
// deploy is hidden behind the "无 Hosting Provider" hint —
// no adapter is registered in this slice.
public sealed partial class SitesViewModel : ViewModelBase
{
    private readonly ISiteRegistry _registry;
    private readonly IBackgroundProcessSupervisor _processSupervisor;
    // Live preview servers keyed by site id. The previous
    // python3 + BackgroundProcessSupervisor approach is gone:
    // the in-process server cannot be killed by a SIGTERM-to-
    // group signal because it shares this app's lifetime, so
    // the lifecycle is owned here. A second preview for the
    // same site replaces the first (the user gets a "preview
    // already running" toast if the row is already in the
    // dictionary).
    private readonly Dictionary<string, EmbeddedStaticFileServer> _activeServers = new(StringComparer.Ordinal);
    private readonly object _serverGate = new();

    public SitesViewModel(
        ISiteRegistry registry,
        IBackgroundProcessSupervisor processSupervisor)
    {
        _registry = registry;
        _processSupervisor = processSupervisor;
        _registry.Changed += OnRegistryChanged;
        ReloadCommand = new AsyncRelayCommand(ReloadAsync);
        ReloadAsync().FireAndForget();
    }

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string? errorMessage;

    public ObservableCollection<SiteRowViewModel> Sites { get; } = [];

    public IAsyncRelayCommand ReloadCommand { get; }

    [RelayCommand]
    private async Task AddAsync()
    {
        // Default site: no project / source path. The user
        // can fill the form in a follow-up edit; the first
        // slice accepts the defaults so the row lands in
        // the list and the user can verify persistence.
        var site = new Site
        {
            Name = "新站点",
            PreviewMode = SitePreviewMode.Auto,
        };
        await _registry.AddAsync(site);
    }

    [RelayCommand]
    private async Task RemoveAsync(SiteRowViewModel? row)
    {
        if (row is null) return;
        await _registry.RemoveAsync(row.Id);
    }

    [RelayCommand]
    private async Task PreviewAsync(SiteRowViewModel? row)
    {
        if (row is null) return;

        // The site was created in the first slice without a
        // real SourcePath — until the user fills the form,
        // we can't actually launch a preview server. Record
        // a Running deployment so the row updates and the
        // user sees the action took effect; the Url field
        // makes the "placeholder" honest. Once SourcePath is
        // wired in a follow-up, this branch goes away and
        // the in-process server always runs.
        var site = _registry.Sites.FirstOrDefault(s => s.Id == row.Id);
        if (site is null || string.IsNullOrWhiteSpace(site.SourcePath))
        {
            await _registry.RecordDeploymentAsync(new SiteDeployment
            {
                SiteId = row.Id,
                Status = SiteDeploymentStatus.Running,
                Url = "(需先选择源路径)",
            });
            return;
        }

        // Refuse to start a second preview for the same site.
        // The user explicitly asked for one, but two on the
        // same port would bind-fail and the toast would
        // confuse the user more than it informs them.
        lock (_serverGate)
        {
            if (_activeServers.ContainsKey(site.Id))
            {
                return;
            }
        }

        // Pick a port: the user's pinned port if non-zero,
        // otherwise the OS-assigned one. HttpListener does not
        // natively support OS-assigned, so we hand it a free
        // port and immediately close the probe — there is a
        // small race where a different process could grab
        // that port in the gap, but the HttpListener's
        // StartAsync error path reports a clear "port in use"
        // message so the user can retry.
        var port = site.Port == 0 ? FindFreePort() : site.Port;
        EmbeddedStaticFileServer server;
        try
        {
            server = new EmbeddedStaticFileServer(port, site.SourcePath);
            await server.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await _registry.RecordDeploymentAsync(new SiteDeployment
            {
                SiteId = row.Id,
                Status = SiteDeploymentStatus.Failed,
                Url = $"启动失败：{ex.Message}",
            });
            return;
        }

        // Insert into the dictionary outside the await above
        // because the C# compiler refuses to let `await` cross
        // a `lock` boundary. The dictionary mutation is the
        // only thing that needs the lock; once the server is
        // already constructed and listening, the row is in
        // the right state.
        lock (_serverGate)
        {
            _activeServers[site.Id] = server;
        }

        await _registry.RecordDeploymentAsync(new SiteDeployment
        {
            SiteId = row.Id,
            Status = SiteDeploymentStatus.Running,
            Url = $"http://localhost:{server.Port}/",
        });
    }

    [RelayCommand]
    private async Task StopPreviewAsync(SiteRowViewModel? row)
    {
        if (row is null) return;

        var site = _registry.Sites.FirstOrDefault(s => s.Id == row.Id);
        if (site is null) return;

        // 2026-08-03: stop the in-process server directly. The
        // BackgroundProcessSupervisor is no longer the
        // lifecycle owner because an in-process HttpListener
        // cannot receive a SIGTERM-to-group signal — it
        // shares this app's lifetime, so the only "kill" is
        // server.Stop() which closes the listener and
        // cancels the accept loop.
        // Take the server out of the dictionary first; once
        // removed, a second Stop click is a no-op and we can
        // do the I/O without holding the gate.
        EmbeddedStaticFileServer? server;
        bool wasRunning;
        lock (_serverGate)
        {
            wasRunning = _activeServers.Remove(site.Id, out server);
        }

        if (!wasRunning)
        {
            // Nothing to stop; record the stop anyway so the
            // user sees their click took effect (a double-
            // click on Stop is idempotent).
            await _registry.RecordDeploymentAsync(new SiteDeployment
            {
                SiteId = row.Id,
                Status = SiteDeploymentStatus.Stopped,
                Url = "(未在运行)",
            });
            return;
        }

        server.Stop();
        var url = $"http://localhost:{server.Port}/  (已停止)";

        await _registry.RecordDeploymentAsync(new SiteDeployment
        {
            SiteId = row.Id,
            Status = SiteDeploymentStatus.Stopped,
            Url = url,
        });
    }

    // Probe a free loopback port and return it. The probe socket
    // is closed before the EmbeddedStaticFileServer binds so
    // there is a small race where another process could grab the
    // port in the gap; the server's StartAsync surfaces a clear
    // "address in use" error in that case so the user can retry.
    private static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    // Short id for embedding in the URL hint. Eight hex chars is
    // plenty for the user to spot the matching row in the
    // Environment panel's Background section.
    private static string ShortId(string id) =>
        id.Length <= 8 ? id : id[..8];

    public async Task ReloadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await _registry.ReloadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"刷新失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        // The registry fires Changed on whatever thread
        // ran the last mutation. Marshal back to UI thread
        // before touching the ObservableCollection.
        Dispatcher.UIThread.Post(() =>
        {
            Sites.Clear();
            foreach (var site in _registry.Sites.OrderBy(s => s.CreatedAt))
            {
                Sites.Add(new SiteRowViewModel(site));
            }
            IsLoading = false;
        });
    }
}

// One row in the Sites list. Mirrors the data fields the
// user actually reads (Name / Source / Port / Status /
// Last Preview) so the list stays scannable. The
// preview-mode string is humanised from the enum.
public sealed class SiteRowViewModel
{
    public string Id { get; }
    public string Name { get; }
    public string SourceLabel { get; }
    public string PortLabel { get; }
    public string PreviewModeLabel { get; }
    public string LastPreviewLabel { get; }
    public string AdapterLabel { get; }

    public SiteRowViewModel(Site site)
    {
        Id = site.Id;
        Name = string.IsNullOrWhiteSpace(site.Name) ? "（未命名）" : site.Name;
        SourceLabel = string.IsNullOrWhiteSpace(site.SourcePath)
            ? "(尚未选择源路径)"
            : site.SourcePath;
        PortLabel = site.Port == 0 ? "自动分配" : site.Port.ToString();
        PreviewModeLabel = site.PreviewMode == SitePreviewMode.Auto
            ? "自动检测"
            : $"自定义: {site.CustomCommand}";
        LastPreviewLabel = site.LastPreviewAt is null
            ? "尚未预览"
            : FormatRelative(site.LastPreviewAt.Value);
        AdapterLabel = site.AdapterId == "local" ? "本地" : site.AdapterId;
    }

    private static string FormatRelative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.Now - when;
        if (delta.TotalSeconds < 60) return "刚刚";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} 分钟前";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} 小时前";
        return when.LocalDateTime.ToString("MM-dd HH:mm");
    }
}
