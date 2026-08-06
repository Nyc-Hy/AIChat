using System.Reflection;

namespace AIChat.Tests.Avalonia;

// Wave 11 follow-up: pin the user-visible modal labels
// the XAMLs committed to. A regression that changes "记录运行"
// back to "运行" (the old misleading label) or removes the
// "Wave 9 first slice" tooltip would break the honest-
// placeholder contract without surfacing anywhere in the
// test suite.
//
// Reading the XAML as embedded resource is fragile
// (AvaloniaResource vs source file path differs), so the
// test reads the source file directly via the project
// layout. The .csproj has the Views directory included
// as `<AvaloniaResource Include="Assets\**" />` but the
// View .axaml files are picked up by Avalonia's XAML
// compiler through the SDK's `Microsoft.NET.Sdk` default
// item globs. Either way, the source file is on disk
// under the test assembly's working directory.
public sealed class ModalLabelGuardTests
{
    [Fact]
    public void ScheduledView_HonestLabelAndTooltip_ForRunNowButton()
    {
        // Locate the source file relative to the test
        // assembly. The .csproj places both projects under
        // the same repo root; the convention is
        // src/AIChat.App.Avalonia/Views/Controls/<Name>.axaml.
        var source = ReadXamlSource("ScheduledView.axaml");

        // Scheduled 模态的 '立即运行' 按钮已重命名为 '记录运行' —
        // 因为 first slice 不真发 agent。回归到 '运行' 算契约破坏。
        Assert.True(source.Contains("记录运行"),
            "Scheduled 模态的 '立即运行' 按钮已重命名为 '记录运行'。");
        // tooltip 必须明确 'Wave 9 first slice — 真实 prompt 执行待 follow-up'。
        Assert.True(source.Contains("Wave 9 first slice"),
            "tooltip 必须明确 'Wave 9 first slice'。");
    }

    [Fact]
    public void PluginsView_UsesUnifiedReloadCommandName()
    {
        // The 3 modal VMs used to have inconsistent
        // command names (PluginsViewModel.RefreshCommand
        // vs ScheduledViewModel.ReloadCommand). The
        // refactor unified them to ReloadCommand. The
        // XAML must match — a regression that re-renames
        // one of them would break the binding.
        var source = ReadXamlSource("PluginsView.axaml");

        Assert.True(source.Contains("ReloadCommand"),
            "PluginsView 应使用统一的 ReloadCommand。");
        Assert.False(source.Contains("RefreshCommand"),
            "PluginsView 之前的 RefreshCommand 已废弃,回归算命名不一致。");
    }

    [Fact]
    public void SitesView_CloudDeployButton_HonestDisabledTooltip()
    {
        // Plan §5.4: when no Hosting Provider is registered,
        // the cloud deploy button must be disabled and the
        // tooltip must explain why. A regression that
        // re-enables the button would break the
        // "no fake cloud success" rule.
        var source = ReadXamlSource("SitesView.axaml");

        Assert.True(source.Contains("IsEnabled=\"False\""),
            "Sites 模态的部署按钮必须 IsEnabled=False 因为 AIChat 没注册 Hosting Provider。");
        Assert.True(source.Contains("Hosting Provider"),
            "tooltip 必须解释 '需 Hosting Provider 适配器 (plan §5.4 暂无)'。");
    }

    [Fact]
    public void EnvironmentPanelView_BackgroundProcessesSection_HiddenByDefault()
    {
        // Plan §7.7: the Background Processes entry must
        // stay hidden until the supervisor is built. The
        // XAML binds IsVisible to ShowBackgroundProcesses
        // (default false on the VM). A regression that
        // drops the binding would re-show a placeholder
        // section with no real backing.
        var source = ReadXamlSource("EnvironmentPanelView.axaml");

        Assert.True(source.Contains("ShowBackgroundProcesses"),
            "Background Processes section 必须保持 IsVisible=\"{Binding ShowBackgroundProcesses}\" — " +
            "Plan §7.7 要求 supervisor 未建前不得展示入口。");
    }

    private static string ReadXamlSource(string fileName)
    {
        // Walk up from the test assembly's location to
        // the repo root and look for the XAML under the
        // expected Views/Controls/ directory. This avoids
        // the brittle embedded-resource path that changes
        // with the Avalonia XAML compiler's output layout.
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyLocation)!);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AIChat.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException(
                "Could not locate AIChat.sln walking up from the test assembly.");
        }

        var path = Path.Combine(
            dir.FullName,
            "src", "AIChat.App.Avalonia", "Views", "Controls", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Expected XAML file at {path}.");
        }
        return File.ReadAllText(path);
    }
}
