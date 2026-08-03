using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Tests.Avalonia;

// The 2-toggle permission model (DefaultAccess + FullAccessEnabled) lives on
// MainWindowViewModel because the composer badge and the settings page both
// bind to it. These tests pin the three-state display + the click-cycle order
// so the visual + persistence contract doesn't drift if the host VM gets
// refactored.
//
// We go through the full DI container (AppHost.Build + InMemoryAppRepository)
// so the test exercises the real wiring: setting a toggle calls
// PersistPermissionSettings → IAppRepository.SaveSettingsAsync. The toast /
// approval / sub-agent callbacks stay inert (the container wires
// Mock.Of<IChatCompletionService> equivalents for them).
public sealed class MainWindowPermissionBadgeTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly InMemoryAppRepository _repository;

    public MainWindowPermissionBadgeTests()
    {
        _repository = new InMemoryAppRepository();
        var services = new ServiceCollection();
        services.AddSingleton<IAppRepository>(_repository);
        _provider = AppHost.Build(services);
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void DefaultState_DefaultAccessOn_FullAccessOff_ShowsDefaultBadge()
    {
        var main = _provider.GetRequiredService<MainWindowViewModel>();

        // AppSettings defaults: DefaultAccess = true, FullAccessEnabled = false.
        Assert.True(main.DefaultAccess);
        Assert.False(main.FullAccessEnabled);
        Assert.Equal("默认访问", main.PermissionBadgeText);
    }

    [Fact]
    public void FullAccessEnabled_TakesPrecedenceOverDefaultAccess()
    {
        var main = _provider.GetRequiredService<MainWindowViewModel>();

        main.FullAccessEnabled = true;
        Assert.True(main.DefaultAccess);
        Assert.True(main.FullAccessEnabled);
        Assert.Equal("完全访问", main.PermissionBadgeText);
        Assert.Contains("无需批准", main.PermissionBadgeTooltip);
    }

    [Fact]
    public void BothTogglesOff_ShowsReadOnlyBadge()
    {
        var main = _provider.GetRequiredService<MainWindowViewModel>();

        main.DefaultAccess = false;
        main.FullAccessEnabled = false;
        Assert.Equal("只读", main.PermissionBadgeText);
        Assert.Contains("不修改项目", main.PermissionBadgeTooltip);
    }

    [Fact]
    public void TogglingDefaultAccess_PersistsToAppSettings()
    {
        var main = _provider.GetRequiredService<MainWindowViewModel>();
        Assert.True(main.DefaultAccess);

        main.DefaultAccess = false;

        // PersistSettingsFireAndForget is fire-and-forget; spin a moment for
        // the in-memory repo to land. The repository's SaveSettingsAsync is
        // synchronous so this is effectively immediate.
        SpinWait.SpinUntil(() => !ReadSettings().DefaultAccess, TimeSpan.FromSeconds(1));
        Assert.False(ReadSettings().DefaultAccess);
        Assert.False(ReadSettings().FullAccessEnabled);
    }

    [Fact]
    public void TogglingFullAccess_PersistsToAppSettings()
    {
        var main = _provider.GetRequiredService<MainWindowViewModel>();

        main.FullAccessEnabled = true;

        SpinWait.SpinUntil(() => ReadSettings().FullAccessEnabled, TimeSpan.FromSeconds(1));
        Assert.True(ReadSettings().FullAccessEnabled);
        Assert.True(ReadSettings().DefaultAccess); // unchanged
    }

    [Fact]
    public void CyclePermissionState_WalksDefaultThenFullThenReadOnlyThenDefault()
    {
        var main = _provider.GetRequiredService<MainWindowViewModel>();

        // State 1: default-access (initial)
        Assert.Equal("默认访问", main.PermissionBadgeText);

        // Click → full-access
        main.CyclePermissionStateCommand.Execute(null);
        Assert.Equal("完全访问", main.PermissionBadgeText);
        Assert.True(main.FullAccessEnabled);

        // Click → read-only (both off)
        main.CyclePermissionStateCommand.Execute(null);
        Assert.Equal("只读", main.PermissionBadgeText);
        Assert.False(main.DefaultAccess);
        Assert.False(main.FullAccessEnabled);

        // Click → back to default-access
        main.CyclePermissionStateCommand.Execute(null);
        Assert.Equal("默认访问", main.PermissionBadgeText);
        Assert.True(main.DefaultAccess);
        Assert.False(main.FullAccessEnabled);
    }

    [Fact]
    public void TogglingDefaultAccess_AlsoFlipsNoWriteMode()
    {
        // The legacy ⌘⇧R shortcut flips NoWriteMode; the host mirrors that
        // onto DefaultAccess so the existing UX keeps working. The reverse
        // mirror is also true: changing DefaultAccess flips NoWriteMode in
        // the other direction, so the tool-approval / read-only hint stays
        // consistent. (See OnDefaultAccessChanged / OnNoWriteModeChanged.)
        var main = _provider.GetRequiredService<MainWindowViewModel>();
        Assert.True(main.DefaultAccess);
        Assert.False(main.NoWriteMode);

        main.DefaultAccess = false;
        Assert.True(main.NoWriteMode);

        main.DefaultAccess = true;
        Assert.False(main.NoWriteMode);
    }

    private AppSettings ReadSettings() => _repository.LoadSettingsAsync().GetAwaiter().GetResult();
}
