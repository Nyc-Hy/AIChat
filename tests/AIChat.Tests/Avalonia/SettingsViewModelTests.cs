using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Tools;
using Moq;

namespace AIChat.Tests.Avalonia;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void DisabledTool_RemainsExplicitAndDefaultPresetRestoresAvailability()
    {
        var registry = AgentToolRegistry.CreateDefault();
        var settings = new AppSettings
        {
            EnabledToolIds = registry.All.Select(tool => tool.Id).ToList()
        };
        var holder = new SettingsHolder();
        holder.Replace(settings);
        var repository = new Mock<IAppRepository>();
        repository
            .Setup(item => item.SaveSettingsAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var viewModel = new SettingsViewModel(holder, repository.Object, registry);
        viewModel.Refresh();

        var row = Assert.Single(viewModel.Tools, item => item.Id == "read_file");
        row.SelectedMode = ToolPermissionMode.Disabled;

        Assert.DoesNotContain("read_file", settings.EnabledToolIds);
        Assert.Equal(ToolPermissionMode.Disabled, settings.ToolPermissionModes["read_file"]);

        viewModel.ApplyDefaultPresetCommand.Execute(null);

        Assert.Contains("read_file", settings.EnabledToolIds);
        Assert.Empty(settings.ToolPermissionModes);
    }
}
