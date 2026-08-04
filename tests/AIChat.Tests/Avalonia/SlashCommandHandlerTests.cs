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
        Assert.Contains("/search", result.Body);
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

    [Fact]
    public void MainWindowViewModel_ImplementsISlashCommandHost()
    {
        // The handler used to take MainWindowViewModel directly, which
        // meant the dependency grew one field at a time. The interface
        // is now the contract; this is a type-level test that locks
        // the implementation in place so a future refactor that drops
        // the interface (and re-couples the handler to the concrete
        // VM) breaks the build.
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();

        Assert.IsAssignableFrom<ISlashCommandHost>(viewModel);
    }

    [Fact]
    public async Task Search_NoQuery_ShowsUsage()
    {
        // /search without a query shows the usage line so the
        // user knows to pass a needle. The body intentionally
        // does not mention a count so a 0-result case looks
        // distinct from a real search.
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();

        var (handled, result) = await SlashCommandHandler.TryExecuteAsync("/search", viewModel);

        Assert.True(handled);
        Assert.NotNull(result);
        Assert.Contains("/search", result!.Body);
    }

    [Fact]
    public async Task Search_EmptyHost_ReportsNoMatches()
    {
        // A fresh AppHost (no sessions loaded) yields a "no
        // matches" body. The 'found' counter must be 0 so the
        // user can tell 'no sessions' from 'no matches'.
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();

        var (handled, result) = await SlashCommandHandler.TryExecuteAsync("/search keychain", viewModel);

        Assert.True(handled);
        Assert.NotNull(result);
        Assert.Contains("没有找到", result!.Body);
    }

    [Fact]
    public async Task Search_HitInTitle_SurfacesFirst()
    {
        // 2026-08-03: title hit should be the top result, with
        // a '(标题命中)' excerpt so the user can tell at a
        // glance that the match was on the title, not the
        // message body. Per-test isolated data root so the
        // SaveSessionsAsync write does not leak into other
        // tests' view of the world.
        using var isolatedRoot = new TestIsolatedDataRoot();
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();
        var session = new AIChat.Domain.Chat.Standalone
        {
            Id = "s-keychain",
            Title = "keychain 弹窗排查",
            UpdatedAt = DateTimeOffset.Now,
            Messages =
            [
                new AIChat.Domain.Chat.ChatMessage
                {
                    Role = AIChat.Domain.Chat.ChatRole.User,
                    Content = "为什么 macOS 一直弹?",
                    CreatedAt = DateTimeOffset.Now,
                },
            ],
        };
        var repo = host.GetRequiredService<AIChat.Abstractions.Persistence.IAppRepository>();
        await repo.SaveSessionsAsync([session]);
        await viewModel.RefreshStandaloneConversationsAsync();

        var (handled, result) = await SlashCommandHandler.TryExecuteAsync("/search keychain", viewModel);

        Assert.True(handled);
        Assert.NotNull(result);
        Assert.Contains("keychain 弹窗排查", result!.Body);
        Assert.Contains("标题命中", result.Body);
    }

    [Fact]
    public async Task Search_HitInMessageContent_ShowsExcerpt()
    {
        // Body match should produce an excerpt window around
        // the match (40 chars before / after, with … markers
        // when truncated), not the full message text. This
        // matters because a long assistant reply would
        // otherwise dominate the result bubble. Per-test
        // isolated data root so SaveSessionsAsync write does
        // not leak.
        using var isolatedRoot = new TestIsolatedDataRoot();
        using var host = AppHost.Build();
        var viewModel = host.GetRequiredService<MainWindowViewModel>();
        var session = new AIChat.Domain.Chat.Standalone
        {
            Id = "s-nested",
            Title = "无关键",
            UpdatedAt = DateTimeOffset.Now,
            Messages =
            [
                new AIChat.Domain.Chat.ChatMessage
                {
                    Role = AIChat.Domain.Chat.ChatRole.Assistant,
                    Content = new string('a', 100) + " MATCHED " + new string('b', 100),
                    CreatedAt = DateTimeOffset.Now,
                },
            ],
        };
        var repo = host.GetRequiredService<AIChat.Abstractions.Persistence.IAppRepository>();
        await repo.SaveSessionsAsync([session]);
        await viewModel.RefreshStandaloneConversationsAsync();

        var (handled, result) = await SlashCommandHandler.TryExecuteAsync("/search MATCHED", viewModel);

        Assert.True(handled);
        Assert.NotNull(result);
        Assert.Contains("无关键", result!.Body);
        // The full message is 211 chars; the excerpt is at most
        // 80 + 2 ellipses. If the full body were in the result
        // the row would dominate the bubble and obscure the
        // other hits.
        Assert.DoesNotContain(new string('a', 100), result.Body);
    }

    // 2026-08-03: per-test isolated data root. AppHost.Build
    // does not set AICHAT_ISOLATED_DATA_ROOT, so the test
    // would otherwise share the user's real settings.json
    // with other test methods. Two /search tests that both
    // call SaveSessionsAsync would race on the same file
    // (one sees the other's session). The disposable
    // isolated root solves both: each test gets a unique
    // temp directory for AppRuntimeProfile.DataDirectory,
    // restored when the test method returns.
    private sealed class TestIsolatedDataRoot : IDisposable
    {
        private readonly string _previousRoot;

        public TestIsolatedDataRoot()
        {
            _previousRoot = Environment.GetEnvironmentVariable("AICHAT_ISOLATED_DATA_ROOT") ?? "";
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "aichat-slash-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Environment.SetEnvironmentVariable("AICHAT_ISOLATED_DATA_ROOT", tempRoot);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("AICHAT_ISOLATED_DATA_ROOT", _previousRoot);
            // The temp directory is best-effort cleaned up by
            // the OS temp cleaner; do not assert because a
            // parallel run may still be reading it.
        }
    }
}
