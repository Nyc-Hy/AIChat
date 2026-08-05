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

    // 2026-08-05: the 1-click "切到 M2.7 试试" affordance
    // on the test-failure row is gone — MiniMax
    // unified the Coding Plan + Token Plan billing
    // surfaces overnight, so sk-cp-… keys now
    // authenticate against M3 too. The remaining
    // 401/403/404 causes (dead key, org lockout, model
    // typo) have no lower-tier fallback. The method
    // is kept (rather than deleted) so the SettingsView
    // binds to a stable surface; the theory pins
    // the "always null" contract so a future tier
    // shift (M4 → M3-highspeed) that re-adds a
    // non-null return shows up in code review.
    [Theory]
    [InlineData("MiniMax-M3", ProviderErrorKind.Authentication)]
    [InlineData("MiniMax-M3-highspeed", ProviderErrorKind.Authentication)]
    [InlineData("MiniMax-M3", ProviderErrorKind.ModelNotFound)]
    [InlineData("MiniMax-M3", ProviderErrorKind.PermissionDenied)]
    [InlineData("MiniMax-M3", ProviderErrorKind.RateLimited)]
    [InlineData("MiniMax-M3", ProviderErrorKind.Timeout)]
    [InlineData("MiniMax-M3", ProviderErrorKind.Server)]
    [InlineData("MiniMax-M3", ProviderErrorKind.Network)]
    public void InferSuggestedFallbackModel_AlwaysReturnsNullAfterM2xRetirement(
        string currentModelId, ProviderErrorKind kind)
    {
        var actual = ProviderConfigViewModel.InferSuggestedFallbackModel(
            currentModelId, kind);

        Assert.Null(actual);
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

    // 2026-08-04: per-model parameter rows surface the
    // catalog's per-knob Options in the Settings modal
    // (the half-finished "Settings 没啥能用的" piece).
    // M3 ships 5 knobs (thinking + reasoning_split +
    // top_p + parallel_tool_calls + response_format);
    // the VM's ModelParameters collection is populated
    // lazily when the user picks a model. Pin the row
    // count + the knob ids so a future catalog tweak
    // (drop a knob, add a knob) breaks the test
    // instead of silently shipping a Settings modal
    // that's missing a row the user expected.
    [Fact]
    public void OnProviderModelChanged_PopulatesM3ParameterRows()
    {
        var (vm, _, _, _) = CreateViewModel();
        vm.Refresh();
        vm.ProviderModel = "MiniMax-M3";

        Assert.Equal(5, vm.ModelParameters.Count);
        Assert.Contains(vm.ModelParameters, row => row.Parameter.Id == "minimax.thinking");
        Assert.Contains(vm.ModelParameters, row => row.Parameter.Id == "minimax.reasoning_split");
        Assert.Contains(vm.ModelParameters, row => row.Parameter.Id == "top_p");
        Assert.Contains(vm.ModelParameters, row => row.Parameter.Id == "parallel_tool_calls");
        Assert.Contains(vm.ModelParameters, row => row.Parameter.Id == "response_format");
    }

    // 2026-08-05: M2.7 dropped from the catalog
    // (Coding Plan keys now auth M3). A user with
    // `model: MiniMax-M2.7` in their settings.json
    // resolves through the "non-empty user-typed
    // id" branch which returns a synthetic model
    // (provider defaults, no parameters). The
    // Settings UI shows the M2.7 id in the textbox
    // but renders an empty parameters list — the
    // M3-only knobs (thinking / parallel_tool_calls /
    // response_format) are absent, and the M2.7-only
    // knobs (reasoning_split / top_p) are also
    // absent (the synthetic model has no Parameters).
    // The OpenAICompatibleChatProvider's switch reads
    // settings.ModelParameters by id though, so a
    // user who already has `top_p=0.5` saved from
    // the M2.7 era still has it sent on the wire.
    [Fact]
    public void OnProviderModelChanged_TypedM27Id_RendersEmptyParameterList()
    {
        var (vm, _, _, _) = CreateViewModel();
        vm.Refresh();
        vm.ProviderModel = "MiniMax-M2.7";

        Assert.Empty(vm.ModelParameters);
    }

    // 2026-08-05: switching from M3 → a user-typed
    // M2.7 id collapses the 5 M3 rows to zero. The
    // "swap model and the parameter UI changes
    // shape" contract holds — a user migrating from
    // M2.7 to M3 (or vice versa) sees the row count
    // track the catalog shape for the active model.
    [Fact]
    public void OnProviderModelChanged_FromM3HighspeedToUserTypedM27_CollapsesToEmptyRows()
    {
        var (vm, _, _, _) = CreateViewModel();
        vm.Refresh();
        vm.ProviderModel = "MiniMax-M3-highspeed";
        Assert.Equal(5, vm.ModelParameters.Count);

        vm.ProviderModel = "MiniMax-M2.7";

        Assert.Empty(vm.ModelParameters);
    }

    // 2026-08-04: each row's SelectedValue is
    // seeded from the user's saved setting
    // (settings.ModelParameters[id]). A user who
    // previously set thinking=enabled and reloads
    // the modal sees the dropdown reflect that
    // choice, not the empty default.
    [Fact]
    public void OnProviderModelChanged_SeedsRowsFromSavedSettings()
    {
        var (vm, _, holder, _) = CreateViewModel();
        holder.Current.ModelParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["minimax.thinking"] = "enabled",
            ["top_p"] = "0.5"
        };
        vm.Refresh();
        vm.ProviderModel = "MiniMax-M3";

        var thinking = Assert.Single(vm.ModelParameters, row => row.Parameter.Id == "minimax.thinking");
        Assert.Equal("enabled", thinking.SelectedValue);

        var topP = Assert.Single(vm.ModelParameters, row => row.Parameter.Id == "top_p");
        Assert.Equal("0.5", topP.SelectedValue);
    }

    // 2026-08-04: the save path writes each row's
    // SelectedValue into settings.ModelParameters
    // (skipping empty / whitespace values — the
    // OpenAI provider's switch treats whitespace
    // as "no override"). Without this, the user
    // could change the dropdowns in the modal
    // but the values would never reach disk.
    [Fact]
    public async Task SaveProvider_WritesParameterRowsIntoSettings()
    {
        var (vm, _, holder, _) = CreateViewModel();
        vm.Refresh();
        vm.ProviderApiKey = "test-key";
        vm.ProviderModel = "MiniMax-M3";
        var thinking = Assert.Single(vm.ModelParameters, row => row.Parameter.Id == "minimax.thinking");
        thinking.SelectedValue = "disabled";
        var topP = Assert.Single(vm.ModelParameters, row => row.Parameter.Id == "top_p");
        topP.SelectedValue = "0.1";

        await vm.SaveProviderCommand.ExecuteAsync(null);

        // The save path runs NormalizeModelParameters
        // which keeps all known-for-current-model
        // entries (the 5 M3 knobs) and drops unknown
        // ones — so we expect exactly the two
        // user-picked knobs to land in
        // holder.Current.ModelParameters.
        Assert.Equal("disabled", holder.Current.ModelParameters["minimax.thinking"]);
        Assert.Equal("0.1", holder.Current.ModelParameters["top_p"]);
    }

    // 2026-08-04: a user who sets a row back to
    // "默认" (the empty string) is asking for the
    // platform default — that means "don't emit
    // the field on the wire", not "save an empty
    // string". The VM's SyncModelParametersToProvider
    // drops the empty entry before
    // NormalizeModelParameterValues runs; the
    // Normalize pass then re-adds the key with its
    // catalog default (also ""). The end state is
    // a present-but-empty key — the OpenAI provider's
    // `IsNullOrWhiteSpace(parameter.Value) continue`
    // skip means the wire output is identical to
    // "missing key" (no field sent). Pin the
    // present-but-empty contract so a future
    // Normalize rewrite that changes the
    // "always-populate" behavior breaks here
    // rather than as a silent change in the
    // on-the-wire parameter set.
    [Fact]
    public async Task SaveProvider_EmptyParameterValuesArePresentButEmpty()
    {
        var (vm, _, holder, _) = CreateViewModel();
        vm.Refresh();
        vm.ProviderApiKey = "test-key";
        vm.ProviderModel = "MiniMax-M3";
        var topP = Assert.Single(vm.ModelParameters, row => row.Parameter.Id == "top_p");
        topP.SelectedValue = ""; // user picked "默认"

        await vm.SaveProviderCommand.ExecuteAsync(null);

        Assert.True(holder.Current.ModelParameters.ContainsKey("top_p"));
        Assert.Equal("", holder.Current.ModelParameters["top_p"]);
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
