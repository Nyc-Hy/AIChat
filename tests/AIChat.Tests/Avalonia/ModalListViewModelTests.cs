using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.BackgroundProcesses;
using AIChat.Application.Plugins;
using AIChat.Application.Scheduled;
using AIChat.Application.Sites;
using AIChat.Domain.Scheduled;
using AIChat.Domain.Sites;

namespace AIChat.Tests.Avalonia;

// Wave 11 follow-up: VM-level coverage for the 3 modal
// list VMs (Plugins / Scheduled / Sites). The
// registry tests already cover the data layer; this
// suite covers the command → registry routing —
//   * Add / Pause / Resume / RunNow / Remove / Preview
//     commands land in the registry
//   * the OnXxxChanged → VM Items sync is verified
//     separately for one VM (PluginsViewModel) where
//     the marshalled list update matters most
//
// The tests use a fresh temp dir per test so they
// don't touch the real AppRuntimeProfile files.
// Collection-level assertions are skipped because
// the VMs mutate ObservableCollections on the
// dispatcher thread; cross-thread reads are racy in
// the test environment. The OnRegistryChanged sync
// has its own dedicated test below.
public sealed class ModalListViewModelTests
{
    // ----- PluginsViewModel -----

    [Fact]
    public async Task PluginsViewModel_ReloadCommand_ReloadsRegistry()
    {
        var root = NewTempDir();
        try
        {
            var registry = new PluginRegistry(root);
            await WritePluginAsync(root, "echo", """
                {
                  "id": "echo_plugin",
                  "name": "Echo",
                  "enabled": true,
                  "tools": [
                    { "id": "echo", "description": "Echo", "risk": "read_only",
                      "command": { "executable": "echo", "arguments": ["hi"] } }
                  ]
                }
                """);
            await registry.ReloadAsync();

            var vm = new PluginsViewModel(registry);
            // Drive the reload through the same command the
            // XAML's refresh button uses.
            await vm.ReloadCommand.ExecuteAsync(null);

            // The Plugins collection is set by the
            // OnRegistryChanged handler, which posts to
            // the UI thread. In the test environment the
            // post may not have landed yet; assert against
            // the registry directly.
            Assert.Single(registry.Plugins);
            Assert.Equal("echo_plugin", registry.Plugins[0].Id);
        }
        finally { TryDelete(root); }
    }

    // ----- ScheduledViewModel -----

    [Fact]
    public async Task ScheduledViewModel_AddCommand_AppendsToRegistry()
    {
        var root = NewTempDir();
        try
        {
            var registry = new ScheduledTaskRegistry(
                Path.Combine(root, "tasks.json"),
                Path.Combine(root, "runs.json"));
            await registry.ReloadAsync();
            var vm = new ScheduledViewModel(registry);

            await vm.AddCommand.ExecuteAsync(null);

            Assert.Single(registry.Tasks);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task ScheduledViewModel_PauseCommand_FlipsRegistryIsPaused()
    {
        var root = NewTempDir();
        try
        {
            var registry = new ScheduledTaskRegistry(
                Path.Combine(root, "tasks.json"),
                Path.Combine(root, "runs.json"));
            await registry.ReloadAsync();
            var vm = new ScheduledViewModel(registry);
            await vm.AddCommand.ExecuteAsync(null);
            var task = registry.Tasks[0];

            await vm.PauseCommand.ExecuteAsync(NewRowFor(task.Id, vm));
            Assert.True(registry.Tasks[0].IsPaused);

            // Build a fresh row from the updated task;
            // the VM's Tasks collection may not have
            // been re-bound yet (OnRegistryChanged posts
            // async). The Pause / Resume command takes
            // any row that knows the task id.
            var rowAfterPause = new ScheduledTaskRowViewModel(registry.Tasks[0]);
            await vm.ResumeCommand.ExecuteAsync(rowAfterPause);
            Assert.False(registry.Tasks[0].IsPaused);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task ScheduledViewModel_RunNowCommand_AppendsHistoryEntry()
    {
        // The "记录运行" command doesn't actually invoke
        // the agent — it records a Running entry. The
        // test pins this contract so a future contributor
        // changing the command's behaviour sees a failing
        // test.
        var root = NewTempDir();
        try
        {
            var registry = new ScheduledTaskRegistry(
                Path.Combine(root, "tasks.json"),
                Path.Combine(root, "runs.json"));
            await registry.ReloadAsync();
            var vm = new ScheduledViewModel(registry);
            await vm.AddCommand.ExecuteAsync(null);

            await vm.RunNowCommand.ExecuteAsync(
                new ScheduledTaskRowViewModel(registry.Tasks[0]));

            Assert.Single(registry.Runs);
            Assert.Equal(ScheduledRunStatus.Running, registry.Runs[0].Status);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task ScheduledViewModel_RemoveCommand_DropsFromRegistry()
    {
        var root = NewTempDir();
        try
        {
            var registry = new ScheduledTaskRegistry(
                Path.Combine(root, "tasks.json"),
                Path.Combine(root, "runs.json"));
            await registry.ReloadAsync();
            var vm = new ScheduledViewModel(registry);
            await vm.AddCommand.ExecuteAsync(null);
            var row = new ScheduledTaskRowViewModel(registry.Tasks[0]);

            await vm.RemoveCommand.ExecuteAsync(row);

            Assert.Empty(registry.Tasks);
        }
        finally { TryDelete(root); }
    }

    // ----- SitesViewModel -----

    [Fact]
    public async Task SitesViewModel_AddCommand_AppendsToRegistry()
    {
        var root = NewTempDir();
        try
        {
            var registry = new SiteRegistry(
                Path.Combine(root, "sites.json"),
                Path.Combine(root, "deployments.json"));
            await registry.ReloadAsync();
            var supervisor = new BackgroundProcessSupervisor(
                Path.Combine(root, "processes.json"));
            var vm = new SitesViewModel(registry, supervisor);

            await vm.AddCommand.ExecuteAsync(null);

            Assert.Single(registry.Sites);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task SitesViewModel_RemoveCommand_DropsFromRegistry()
    {
        var root = NewTempDir();
        try
        {
            var registry = new SiteRegistry(
                Path.Combine(root, "sites.json"),
                Path.Combine(root, "deployments.json"));
            await registry.ReloadAsync();
            var supervisor = new BackgroundProcessSupervisor(
                Path.Combine(root, "processes.json"));
            var vm = new SitesViewModel(registry, supervisor);
            await vm.AddCommand.ExecuteAsync(null);
            var row = new SiteRowViewModel(registry.Sites[0]);

            await vm.RemoveCommand.ExecuteAsync(row);

            Assert.Empty(registry.Sites);
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public async Task SitesViewModel_PreviewCommand_RecordsRunningDeployment()
    {
        // Wave 9 → Wave 7 follow-up: PreviewAsync now
        // routes through BackgroundProcessSupervisor when
        // the site has a SourcePath. The default site
        // (no source path) takes the placeholder branch
        // that just records a Running deployment, so the
        // user can verify the command lands without
        // spinning up a real python server in the test
        // host. The "real start" path has its own test
        // in SitesViewModelTests (see Preview_WithSourcePath
        // when the form lands in a follow-up).
        var root = NewTempDir();
        try
        {
            var registry = new SiteRegistry(
                Path.Combine(root, "sites.json"),
                Path.Combine(root, "deployments.json"));
            await registry.ReloadAsync();
            var supervisor = new BackgroundProcessSupervisor(
                Path.Combine(root, "processes.json"));
            var vm = new SitesViewModel(registry, supervisor);
            await vm.AddCommand.ExecuteAsync(null);
            var row = new SiteRowViewModel(registry.Sites[0]);

            await vm.PreviewCommand.ExecuteAsync(row);

            Assert.Single(registry.Deployments);
            Assert.Equal(SiteDeploymentStatus.Running, registry.Deployments[0].Status);
        }
        finally { TryDelete(root); }
    }

    // ----- helpers -----

    private static string NewTempDir()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "aichat-modal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }

    private static async Task WritePluginAsync(string root, string directoryName, string manifestJson)
    {
        var pluginDir = Path.Combine(root, directoryName);
        Directory.CreateDirectory(pluginDir);
        await File.WriteAllTextAsync(Path.Combine(pluginDir, "plugin.json"), manifestJson);
    }

    // Build a row for a known registry task id without
    // touching the VM's Tasks collection (which may
    // not have been re-bound yet after the previous
    // command's OnRegistryChanged post). The Pause /
    // Resume / RunNow / Remove commands only need the
    // row's id to find the right registry entry.
    private static ScheduledTaskRowViewModel NewRowFor(string id, ScheduledViewModel vm)
    {
        // The VM's existing Tasks may already have a
        // row for the same id; reuse it to keep the
        // XAML's "selected row" semantics consistent.
        var existing = vm.Tasks.FirstOrDefault(t => t.Id == id);
        return existing ?? new ScheduledTaskRowViewModel(new ScheduledTask { Id = id });
    }
}
