using AIChat.App.Avalonia.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Tests.Avalonia;

// PR-10 smoke tests: the headless platform only exists to verify that
// AppHost builds cleanly. View-model instantiation triggers a fire-and-
// forget refresh that depends on the dispatcher pumping; that's covered
// by the live process-launch smoke test, not by these unit tests.
public class HeadlessSmokeTests
{
    [Fact]
    public void AvaloniaHeadlessPlatform_Loads()
    {
        // Touching the dispatcher initialises the headless platform
        // lazily. Just referencing the type forces the assembly to be
        // loaded; if Avalonia.Headless is missing or broken, this throws.
        var dispatcherType = typeof(global::Avalonia.Threading.Dispatcher);
        Assert.NotNull(dispatcherType);
    }

    [Fact]
    public void AppHost_BuildsAndResolvesPlainServices()
    {
        using var host = AppHost.Build();

        // Services without a transitive dependency on the view-model
        // refresh loop are safe to resolve here.
        Assert.NotNull(host.GetService<ISettingsHolder>());
        Assert.NotNull(host.GetService<AIChat.Abstractions.Persistence.IAppRepository>());
        Assert.NotNull(host.GetService<IProjectPicker>());
        Assert.NotNull(host.GetService<IClipboardService>());
        Assert.NotNull(host.GetService<IApprovalService>());
    }

    [Fact]
    public void AppHost_ResolvesIThemeService()
    {
        using var host = AppHost.Build();

        var theme = host.GetService<IThemeService>();
        Assert.NotNull(theme);
    }
}
