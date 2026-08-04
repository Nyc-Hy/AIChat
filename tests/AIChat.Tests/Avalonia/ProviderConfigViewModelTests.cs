using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.App.Avalonia.ViewModels;
using AIChat.Application.Llm.Routing;
using Moq;

namespace AIChat.Tests.Avalonia;

// Unit tests for the PR-2 extraction. They cover the cross-VM contract
// (events raised, settings persisted) without booting Avalonia itself —
// ProviderConfigViewModel's constructor only touches pure CLR types, so
// the tests do not need the headless platform.
public class ProviderConfigViewModelTests
{
    [Fact]
    public void Refresh_PopulatesProviderTemplatesAndAdvancedProviders()
    {
        var (vm, _, _, _) = CreateViewModel();

        vm.Refresh();

        Assert.NotEmpty(vm.ProviderTemplates);
        Assert.NotEmpty(vm.AdvancedProviders);
        Assert.NotNull(vm.SelectedProviderTemplate);
    }

    // 2026-08-04: the 1-click "切到 M2.7 试试" affordance
    // on the test-failure row. Pure static helper —
    // the input is (currentModelId, errorKind) and
    // the output is either a fallback model id from
    // the catalog (M3 → M2.7 for auth/model-not-found
    // errors) or null (transient / network / config
    // errors where a model swap doesn't help). The
    // "internal" visibility mirrors the class's
    // existing helper pattern.
    [Theory]
    [InlineData("MiniMax-M3", ProviderErrorKind.Authentication, "MiniMax-M2.7")]
    [InlineData("MiniMax-M3-highspeed", ProviderErrorKind.Authentication, "MiniMax-M2.7")]
    [InlineData("MiniMax-M3", ProviderErrorKind.ModelNotFound, "MiniMax-M2.7")]
    [InlineData("MiniMax-M3", ProviderErrorKind.PermissionDenied, "MiniMax-M2.7")]
    [InlineData("MiniMax-M2.7", ProviderErrorKind.Authentication, null)]
    [InlineData("MiniMax-M3", ProviderErrorKind.RateLimited, null)]
    [InlineData("MiniMax-M3", ProviderErrorKind.Timeout, null)]
    [InlineData("MiniMax-M3", ProviderErrorKind.Server, null)]
    [InlineData("MiniMax-M3", ProviderErrorKind.Network, null)]
    public void InferSuggestedFallbackModel_PicksLowerTierForTierErrors(
        string currentModelId, ProviderErrorKind kind, string? expected)
    {
        // The method is `internal` on
        // ProviderConfigViewModel — AIChat.Tests
        // has InternalsVisibleTo so the test can
        // call it directly. The test pins the
        // routing logic so a future regression
        // (e.g. dropping the M2.7 fallback on
        // M3 auth errors, or suggesting M2.7 on
        // a 5xx transient) breaks the build
        // rather than as a daily-driver "the
        // suggestion is wrong" support ticket.
        var actual = ProviderConfigViewModel.InferSuggestedFallbackModel(
            currentModelId, kind);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SaveProvider_WithoutSelectedTemplate_RaisesSavedWithError()
    {
        var (vm, _, _, saved) = CreateViewModel();
        vm.SelectedProviderTemplate = null;

        await vm.SaveProviderCommand.ExecuteAsync(null);

        var args = Assert.Single(saved);
        Assert.Equal("请选择模型提供方。", args.ErrorMessage);
    }

    [Fact]
    public async Task SaveProvider_WithoutApiKey_RaisesSavedWithError()
    {
        var (vm, _, _, saved) = CreateViewModel();
        vm.Refresh();
        vm.SelectedProviderTemplate = vm.ProviderTemplates[0];
        vm.ProviderApiKey = "";

        await vm.SaveProviderCommand.ExecuteAsync(null);

        var args = Assert.Single(saved);
        Assert.Equal("请输入 API Key。", args.ErrorMessage);
    }

    [Fact]
    public async Task SaveProvider_WithValidInput_PersistsAndRaisesSaved()
    {
        var (vm, repository, holder, saved) = CreateViewModel();
        vm.Refresh();
        vm.SelectedProviderTemplate = vm.ProviderTemplates[0];
        vm.ProviderApiKey = "test-key";

        await vm.SaveProviderCommand.ExecuteAsync(null);

        // The API key is cleared after a successful save so the user can
        // see at a glance that the credential was committed.
        Assert.Equal("", vm.ProviderApiKey);

        var args = Assert.Single(saved);
        Assert.Null(args.ErrorMessage);
        Assert.NotNull(args.ProviderName);
        Assert.NotEmpty(args.ModelId);
        Assert.Same(holder.Current, args.Settings);

        Mock.Get(repository).Verify(
            repo => repo.SaveSettingsWithSecretsAsync(holder.Current, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TestProvider_WithoutConfiguredProvider_RaisesFailedTestOnly()
    {
        var (vm, _, _, _, started, completed) = CreateViewModelWithTestCapture();

        await vm.TestProviderCommand.ExecuteAsync(null);

        Assert.Empty(started);
        var args = Assert.Single(completed);
        Assert.False(args.IsSuccess);
        Assert.Equal("没有可测试的 API Key。", args.Message);
    }

    [Fact]
    public async Task TestProvider_WithConfiguredProvider_RaisesStartedThenCompleted()
    {
        var (vm, _, holder, _, started, completed) = CreateViewModelWithTestCapture();

        // Configure a provider with an API key so the command proceeds to
        // the actual connection test. The real tester will fail against
        // a fake URL, but the test only asserts that the events fire in
        // the right order with the right names.
        var firstTemplate = ChatProviderCatalog.All[0];
        ProviderSettingsService.AddConfiguredProvider(holder.Current, firstTemplate.Id, "any-key");

        await vm.TestProviderCommand.ExecuteAsync(null);

        var startArgs = Assert.Single(started);
        Assert.Equal(firstTemplate.Name, startArgs.ProviderName);
        var doneArgs = Assert.Single(completed);
        Assert.False(doneArgs.IsSuccess);
    }

    private static (ProviderConfigViewModel vm, IAppRepository repository, SettingsHolder holder, List<ProviderSavedEventArgs> saved)
        CreateViewModel()
    {
        var repository = Mock.Of<IAppRepository>(repo =>
            repo.SaveSettingsAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var holder = new SettingsHolder();
        holder.Replace(new AppSettings());
        var vm = new ProviderConfigViewModel(repository, new ProviderConnectionTester(), holder);
        var saved = new List<ProviderSavedEventArgs>();
        vm.Saved += (_, args) => saved.Add(args);
        return (vm, repository, holder, saved);
    }

    private static (ProviderConfigViewModel vm, IAppRepository repository, SettingsHolder holder,
        List<ProviderSavedEventArgs> saved, List<ProviderTestStartedEventArgs> started, List<ProviderTestCompletedEventArgs> completed)
        CreateViewModelWithTestCapture()
    {
        var repository = Mock.Of<IAppRepository>(repo =>
            repo.SaveSettingsAsync(It.IsAny<AppSettings>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var holder = new SettingsHolder();
        holder.Replace(new AppSettings());
        var vm = new ProviderConfigViewModel(repository, new ProviderConnectionTester(), holder);
        var saved = new List<ProviderSavedEventArgs>();
        var started = new List<ProviderTestStartedEventArgs>();
        var completed = new List<ProviderTestCompletedEventArgs>();
        vm.Saved += (_, args) => saved.Add(args);
        vm.TestStarted += (_, args) => started.Add(args);
        vm.TestCompleted += (_, args) => completed.Add(args);
        return (vm, repository, holder, saved, started, completed);
    }
}
