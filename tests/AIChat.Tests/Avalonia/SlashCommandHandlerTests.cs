using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AIChat.Tests.Avalonia;

// Smoke tests for the slash command surface. BuildStatus is the only
// non-trivial formatter in the handler; the other commands either
// delegate to host methods (covered elsewhere) or return static
// help text. Pinning the /status output keeps accidental rewording
// out of the daily driver.
public class SlashCommandHandlerTests
{
    [Fact]
    public async Task Status_WithNoProject_ShowsUnifiedUnselectedLine()
    {
        // SelectedProjectName defaults to "未选择项目" when the
        // sidebar hasn't loaded anything. The earlier code only
        // mapped the "未配置路径" sentinel to "(未选择)", so an empty
        // project would render as "项目: 未选择项目" — redundant
        // "未选择" on both sides of the colon. The fix collapses
        // both sentinels to one line.
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();

        var (handled, result) = await SlashCommandHandler.TryExecuteAsync("/status", viewModel);

        Assert.True(handled);
        Assert.NotNull(result);
        Assert.Contains("项目: (未选择)", result!.Body);
        Assert.DoesNotContain("项目: 未选择项目", result.Body);
    }

    [Fact]
    public async Task Help_ListsEveryBuiltInCommand()
    {
        // The /help text is the user-facing list of slash commands.
        // If a new command is added or an old one is dropped, the
        // text has to follow — otherwise the user is told a command
        // exists when it doesn't, or vice versa.
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();

        var (handled, result) = await SlashCommandHandler.TryExecuteAsync("/help", viewModel);

        Assert.True(handled);
        Assert.NotNull(result);
        Assert.Contains("/clear", result!.Body);
        Assert.Contains("/new", result.Body);
        Assert.Contains("/status", result.Body);
        Assert.Contains("/memory", result.Body);
        Assert.Contains("/git", result.Body);
        Assert.Contains("/copy", result.Body);
        Assert.Contains("/help", result.Body);
    }

    [Fact]
    public async Task UnknownCommand_ReturnsFriendlyError()
    {
        // The /xxx fallback keeps the user from typing a typo and
        // getting a confusing "agent did nothing" experience. The
        // response names the bad command so the user can see what
        // was rejected.
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();

        var (handled, result) = await SlashCommandHandler.TryExecuteAsync("/banana", viewModel);

        Assert.True(handled);
        Assert.NotNull(result);
        Assert.Contains("/banana", result!.Body);
        Assert.Contains("/help", result.Body);
    }

    [Fact]
    public async Task NonSlashPrompt_IsNotHandled()
    {
        // The handler is a gate — the host only consults it when
        // the user might have meant a command. A non-slash prompt
        // must come back unhandled so the host's normal agent-run
        // path takes over.
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();

        var (handled, _) = await SlashCommandHandler.TryExecuteAsync("hello world", viewModel);

        Assert.False(handled);
    }
}
