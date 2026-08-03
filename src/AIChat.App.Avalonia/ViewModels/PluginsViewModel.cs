using AIChat.Application.Plugins;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

// Wave 8 (parity plan §7 Wave 8): the right-pane (modal)
// view-model for the Plugins tab. Shows the list of plugins
// the registry has loaded, the loader diagnostics, and a
// Refresh button. Per-plugin toggle (enable / disable) and
// detail inspection land in a follow-up slice; this first
// pass is "can the user see what's installed and reload after
// copying a new plugin folder" — the read path is the bigger
// UX win and the rest rides on the same registry.
public sealed partial class PluginsViewModel : ViewModelBase
{
    private readonly IPluginRegistry _registry;

    public PluginsViewModel(IPluginRegistry registry)
    {
        _registry = registry;
        _registry.Changed += OnRegistryChanged;
        ReloadCommand = new AsyncRelayCommand(RefreshAsync);
        RefreshAsync().FireAndForget();
    }

    // Path the registry scans. Surfaced in the modal so the
    // user can see "where do I drop plugin folders" without
    // digging through the docs.
    public string PluginsDirectory => _registry.PluginsDirectory;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private IReadOnlyList<PluginSummary> plugins = [];

    [ObservableProperty]
    private IReadOnlyList<string> diagnostics = [];

    [ObservableProperty]
    private string? errorMessage;

    public IAsyncRelayCommand ReloadCommand { get; }

    private async Task RefreshAsync()
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
        // The host dispatcher owns UI-thread mutation. The
        // registry fires Changed on whatever thread ran the
        // last operation; we marshal back here.
        Dispatcher.UIThread.Post(() =>
        {
            Plugins = _registry.Plugins
                .Select(p => new PluginSummary(p))
                .ToList();
            Diagnostics = _registry.Diagnostics
                .Select(d => $"[{d.Severity}] {d.Message}")
                .ToList();
            IsLoading = false;
        });
    }
}

// Display shape for a single plugin row in the modal. Mirrors
// the manifest fields the user actually cares about (id / name
// / version / description / tool count) and flattens the
// `tools` collection into a count so the list stays scannable.
public sealed class PluginSummary
{
    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string Description { get; }
    public int ToolCount { get; }
    public string RiskBreakdown { get; }
    public string DirectoryPath { get; }

    public PluginSummary(PluginManifest manifest)
    {
        Id = manifest.Id;
        Name = manifest.Name;
        Version = string.IsNullOrWhiteSpace(manifest.Version) ? "0.0.0" : manifest.Version;
        Description = manifest.Description;
        ToolCount = manifest.Tools.Count;
        DirectoryPath = manifest.DirectoryPath;
        RiskBreakdown = BuildRiskBreakdown(manifest);
    }

    private static string BuildRiskBreakdown(PluginManifest manifest)
    {
        if (manifest.Tools.Count == 0)
        {
            return "无工具";
        }
        var byRisk = manifest.Tools
            .GroupBy(tool => string.IsNullOrWhiteSpace(tool.Risk) ? "shell" : tool.Risk)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}: {group.Count()}");
        return string.Join(" · ", byRisk);
    }
}

// Tiny helper to keep the fire-and-forget pattern obvious at
// the call site (it's the same shape as
// Avalonia.Threading.Dispatcher.UIThread.Post but with a Task
// return so the caller can opt into awaiting).
internal static class TaskFireAndForgetExtensions
{
    public static async void FireAndForget(this Task task)
    {
        try { await task; } catch { /* surface via the VM's ErrorMessage in a follow-up slice */ }
    }
}
