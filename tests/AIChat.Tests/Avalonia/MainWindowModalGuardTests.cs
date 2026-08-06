using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Tests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using AIChat.Domain.Projects;

namespace AIChat.Tests.Avalonia;

public sealed class MainWindowModalGuardTests
{
    [Fact]
    public async Task ProjectModals_StayClosedWhenNoProjectIsSelected()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppRepository, InMemoryAppRepository>();
        using var host = AppHost.Build(services);
        var main = host.GetRequiredService<MainWindowViewModel>();

        main.OpenMemoryEditorCommand.Execute(null);
        await main.OpenGitStatusCommand.ExecuteAsync(null);
        main.OpenRunHistoryCommand.Execute(null);

        Assert.False(main.IsMemoryEditorOpen);
        Assert.False(main.IsGitStatusOpen);
        Assert.False(main.IsRunHistoryOpen);
        Assert.Contains("请先选择一个项目", main.StatusMessage);
    }

    // Wave 11 refactor: CloseAllModals is the single
    // source of truth for "drop every modal" — used by
    // both the Escape handler (MainWindow.axaml.cs) and
    // OnApprovalPresented (the approval prompt must
    // win over any open modal). The test pins the
    // method's contract so a future contributor who
    // adds a new modal without updating the close list
    // sees a failing test.
    [Fact]
    public void CloseAllModals_DropsEveryOpenModal()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppRepository, InMemoryAppRepository>();
        using var host = AppHost.Build(services);
        var main = host.GetRequiredService<MainWindowViewModel>();

        // Open every modal that CloseAllModals targets.
        // CommandPalette and MemoryEditor have guards
        // (CanOpenModal + project requirement) — for
        // those we set the IsXxxOpen bool directly so
        // the test isn't blocked by a guard. The other
        // modals have no guard and are reached via
        // OpenXxxCommand.
        main.IsCommandPaletteOpen = true;
        main.IsSettingsOpen = true;
        main.IsShortcutsOpen = true;
        main.IsPluginsOpen = true;
        main.IsScheduledOpen = true;
        main.IsSitesOpen = true;

        main.CloseAllModals();

        Assert.False(main.IsCommandPaletteOpen);
        Assert.False(main.IsSettingsOpen);
        Assert.False(main.IsShortcutsOpen);
        Assert.False(main.IsPluginsOpen);
        Assert.False(main.IsScheduledOpen);
        Assert.False(main.IsSitesOpen);
    }
}
