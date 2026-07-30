using System.Collections.ObjectModel;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Agents;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Configuration;
using AIChat.Application.Context;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Projects;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using AIChat.Providers.Anthropic;
using AIChat.Providers.OpenAI;
using AIChat.Storage.Json;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIChat.App.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAppRepository _repository;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly IChatCompletionService _chatService;
    private readonly ProviderConfigViewModel _provider;
    private readonly ProjectSidebarViewModel _sidebar;
    private readonly ConversationListViewModel _conversationList;
    private readonly SessionInsightsViewModel _insights;
    private readonly ToolApprovalViewModel _approvalViewModel;
    private readonly IApprovalService _approval;
    private readonly IThemeService _theme;
    private readonly ISettingsHolder _settingsHolder;
    private readonly IToastService _toast;
    private readonly IProjectPicker _projectPicker;
    private AppSettings _settings = new();

    [ObservableProperty]
    private string activeProvider = "正在加载...";

    [ObservableProperty]
    private string activeModel = "";

    [ObservableProperty]
    private string readiness = "检查中";

    // Computed view-state properties derive from the observables above. Avalonia
    // bindings do not pick up changes to plain CLR properties; we re-raise
    // PropertyChanged manually so the breadcrumb / greeting / status bar update
    // when the underlying fields flip.
    partial void OnActiveProviderChanged(string value)
    {
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(StatusBarModel));
    }
    partial void OnActiveModelChanged(string value) => OnPropertyChanged(nameof(StatusBarModel));
    partial void OnReadinessChanged(string value)
    {
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(NeedsConfiguration));
    }

    [ObservableProperty]
    private string draftPrompt = "";

    [ObservableProperty]
    private string statusMessage = "就绪。";

    [ObservableProperty]
    private string primaryActionText = "准备";

    [ObservableProperty]
    private bool noWriteMode;

    [ObservableProperty]
    private bool autoVerify;

    [ObservableProperty]
    private bool showAdvanced;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendTaskCommand))]
    private bool isRunning;

    // 1.0 Beta: command palette + settings modal overlays. The toggles flip
    // a Border's IsVisible in the MainWindow XAML.
    [ObservableProperty]
    private bool isCommandPaletteOpen;

    [ObservableProperty]
    private bool isSettingsOpen;

    [ObservableProperty]
    private bool hasConversation;

    // 1.0 Beta: derive the top status, breadcrumb visibility and status-bar
    // text from the same handful of fields so the XAML can stay declarative.
    // HasProject hides the project crumb when no project is selected (so
    // the breadcrumb doesn't read "AIChat / 未配置路径"). IsReady /
    // NeedsConfiguration drive the compact status pills.
    public bool HasProject => !string.IsNullOrWhiteSpace(Sidebar.SelectedProjectName)
                              && Sidebar.SelectedProjectName != "未配置路径";

    public bool IsReady => Readiness == "可运行";
    public bool NeedsConfiguration => Readiness == "需要密钥" || Readiness == "需检查";

    public string Greeting => HasProject ? "今天要完成什么？" : "选一个项目开始";
    public string SubGreeting => HasProject
        ? "输入目标后，AIChat 会读取项目上下文并在风险操作前询问你。"
        : "添加本地代码仓库，让 AIChat 读取上下文后再开始任务。";

    public string StatusBarModel => string.IsNullOrEmpty(ActiveModel)
        ? ActiveProvider
        : $"{ActiveProvider} · {ActiveModel}";

    // Approximate context window. 64K covers GPT-4 / Claude / DeepSeek with
    // a single number so the input-area progress bar reads consistently.
    // Will become per-model once the provider API reports the real cap.
    private const int ApproximateContextWindow = 64_000;
    [ObservableProperty]
    private int contextBudgetPercent;

    // Width in DIPs for the inline context meter in the status bar. The mini
    // bar is 80px wide so the percent→width factor is 0.8.
    public double ContextBudgetWidthInMini => Math.Max(0, ContextBudgetPercent * 0.8);

    public ObservableCollection<ActivityItemViewModel> Activity { get; } = [];
    public ObservableCollection<string> SafetyNotes { get; } = [];

    // Toast surface is owned by the IToastService singleton; expose the same
    // ObservableCollection here so the MainWindow XAML can bind via DataContext.
    public ObservableCollection<ToastItem> Toasts => _toast.Toasts;

    // Command palette is its own view-model; the MainWindow binds a search box
    // and an ItemsControl to it.
    public CommandPaletteViewModel CommandPalette { get; } = new();

    // 1.0 Beta: top-level commands the MainWindow code-behind binds to
    // keyboard shortcuts (Cmd+K, Cmd+,). Both flip a single bool so the
    // XAML only has to react to one property change.
    [RelayCommand]
    private void OpenCommandPalette()
    {
        // v1 bug B-2 fix: reset palette state on every open. Previously the
        // second open inherited the previous search text and selected index
        // because the palette's own IsOpen was never written (only this
        // VM's IsCommandPaletteOpen was), so the OnIsOpenChanged partial
        // hook in CommandPaletteViewModel never ran.
        CommandPalette.SearchText = "";
        CommandPalette.SelectedIndex = 0;
        IsCommandPaletteOpen = true;
    }

    [RelayCommand]
    private void CloseCommandPalette() => IsCommandPaletteOpen = false;

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [RelayCommand]
    private void NewConversation()
    {
        ShowNewConversation();
        StatusMessage = "新对话。";
    }

    [RelayCommand]
    private void ToggleTheme() => _theme.CycleToNext();

    // PR-2: provider config surface is delegated to a dedicated view-model.
    public ProviderConfigViewModel Provider => _provider;

    // PR-3: project list / selection lives in a dedicated view-model. The
    // current project is exposed as a public property (CurrentProject) so
    // the rest of the app can read it without going through events.
    public ProjectSidebarViewModel Sidebar => _sidebar;

    // PR-4: recent conversations list and selection live in a dedicated
    // view-model. The activity feed still belongs here; the parent reacts
    // to ConversationSelected events to load messages.
    public ConversationListViewModel ConversationList => _conversationList;

    // PR-5: right-rail "session insights" (context preview + live metrics)
    // live in a dedicated view-model.
    public SessionInsightsViewModel SessionInsights => _insights;

    // PR-6: tool approval dialog and Approve / Reject commands live in a
    // dedicated view-model. The IApprovalService is what the agent
    // harness depends on; the service is a thin facade over the VM.
    public ToolApprovalViewModel Approval => _approvalViewModel;

    public MainWindowViewModel(
        IAppRepository repository,
        AgentToolRegistry toolRegistry,
        IChatCompletionService chatService,
        ProviderConfigViewModel provider,
        ProjectSidebarViewModel sidebar,
        ConversationListViewModel conversationList,
        SessionInsightsViewModel insights,
        ToolApprovalViewModel approvalViewModel,
        IApprovalService approval,
        IThemeService theme,
        ISettingsHolder settingsHolder,
        IToastService toast,
        IProjectPicker projectPicker)
    {
        _repository = repository;
        _toolRegistry = toolRegistry;
        _chatService = chatService;
        _provider = provider;
        _sidebar = sidebar;
        _conversationList = conversationList;
        _insights = insights;
        _approvalViewModel = approvalViewModel;
        _approval = approval;
        _theme = theme;
        _settingsHolder = settingsHolder;
        _toast = toast;
        _projectPicker = projectPicker;

        _provider.Saved += OnProviderSaved;
        _provider.TestStarted += OnProviderTestStarted;
        _provider.TestCompleted += OnProviderTestCompleted;
        _sidebar.ProjectSelected += OnSidebarProjectSelected;
        _sidebar.ProjectAdded += OnSidebarProjectAdded;
        _conversationList.ConversationSelected += OnConversationSelected;
        _approvalViewModel.RequestPresented += OnApprovalPresented;
        _approvalViewModel.RequestResolved += OnApprovalResolved;

        Activity.CollectionChanged += (_, _) => HasConversation = Activity.Count > 0;
        _insights.SessionMetrics.CollectionChanged += (_, _) => UpdateContextBudget();
        _sidebar.PropertyChanged += (_, e) =>
        {
            // SelectedProjectName drives HasProject, Greeting and SubGreeting;
            // re-raise them so the breadcrumb / page title update.
            if (e.PropertyName == nameof(ProjectSidebarViewModel.SelectedProjectName))
            {
                OnPropertyChanged(nameof(HasProject));
                OnPropertyChanged(nameof(Greeting));
                OnPropertyChanged(nameof(SubGreeting));
            }
        };
        RegisterCommandPaletteCommands();

        SeedEmptyState();
        _ = RefreshAsync();
    }

    private void OnSidebarProjectSelected(object? sender, ProjectSelectionChangedEventArgs args)
    {
        _conversationList.Refresh(_sidebar.CurrentProject);
        _insights.PrepareContextPreview(DraftPrompt, _sidebar.CurrentProject, NoWriteMode);
        StatusMessage = args.StatusMessage;
    }

    private void OnSidebarProjectAdded(object? sender, ProjectAddedEventArgs args)
    {
        _conversationList.Refresh(_sidebar.CurrentProject);
        _insights.PrepareContextPreview(DraftPrompt, _sidebar.CurrentProject, NoWriteMode);
        StatusMessage = args.StatusMessage;
    }

    private void OnConversationSelected(object? sender, ConversationSelectedEventArgs args)
    {
        ApplyConversationToActivity(args.Conversation);
        StatusMessage = args.StatusMessage;
    }

    // 1.0 Beta: command palette surface. Each CommandItem carries a
    // lucide-style glyph, a one-line description, the keyboard shortcut
    // hint, and an async action that returns true if the palette should
    // close after running.
    private void RegisterCommandPaletteCommands()
    {
        CommandPalette.RegisterCommands(
        [
            new CommandItem(
                "打开设置",
                "配置模型提供方、API Key、Base URL",
                "⌘ ,",
                "M4 4 H20 V20 H4 Z M9 9 H15 V15 H9 Z",
                () => { IsSettingsOpen = true; return Task.FromResult(true); }),
            new CommandItem(
                "切换主题",
                "在浅色 / 深色 / 跟随系统之间循环",
                "⌘ ⇧ T",
                "M12 4 V2 M12 22 V20 M4 12 H2 M22 12 H20 M5.5 5.5 L4.1 4.1 M19.9 19.9 L18.5 18.5 M5.5 18.5 L4.1 19.9 M19.9 4.1 L18.5 5.5 M12 8 a4 4 0 1 0 0 8 a4 4 0 1 0 0 -8",
                () => { _theme.CycleToNext(); return Task.FromResult(true); }),
            new CommandItem(
                "刷新状态",
                "从本地仓库重新读取项目和会话",
                "F5",
                "M3 12 a9 9 0 1 0 9 -9 a9.75 9.75 0 0 0 -6.74 2.74 L3 8 M3 3 V8 H8 M12 7 V12 L16 14",
                async () => { await RefreshAsync(); return true; }),
            new CommandItem(
                "新建对话",
                "清空当前活动，开始一个全新会话",
                "⌘ N",
                "M12 5 V19 M5 12 H19",
                () => { NewConversation(); return Task.FromResult(true); }),
            new CommandItem(
                "添加项目",
                "从本地选择一个新的代码仓库",
                "⌘ O",
                "M4 4 H20 V20 H4 Z M4 9 H20",
                async () =>
                {
                    var path = await _projectPicker.PickProjectFolderAsync();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        await AddProjectFromUiAsync(path);
                    }
                    return true;
                }),
            new CommandItem(
                "切换只读模式",
                "禁止 AIChat 修改项目中的任何文件",
                "⌘ ⇧ R",
                "M5 12 a7 7 0 1 1 14 0 a7 7 0 1 1 -14 0 M3 3 L21 21",
                () => { NoWriteMode = !NoWriteMode; return Task.FromResult(true); }),
            new CommandItem(
                "切换自动验证",
                "修改完成后自动运行检查命令",
                "⌘ ⇧ V",
                "M5 12 l4 4 L19 6",
                () => { AutoVerify = !AutoVerify; return Task.FromResult(true); }),
            new CommandItem(
                "测试当前模型",
                "发起一次连接性测试，确认 API Key 有效",
                "⌘ T",
                "M3 12 a9 9 0 1 0 18 0 a9 9 0 1 0 -18 0 M12 7 V12 L16 14",
                async () =>
                {
                    await _provider.TestProviderCommand.ExecuteAsync(null);
                    return true;
                }),
            new CommandItem(
                "显示命令面板",
                "搜索命令、动作、设置",
                "⌘ K",
                "M4 4 H20 V20 H4 Z M9 9 H15 V15 H9 Z",
                () => { IsCommandPaletteOpen = true; return Task.FromResult(false); }),
        ]);
    }

    // Called whenever the session insights re-render. Recomputes the
    // context budget percentage for the input-area progress bar.
    private void UpdateContextBudget()
    {
        var approx = (ApproximateContextWindow > 0) ? ApproximateContextWindow : 1;
        var inputTokens = _insights.SessionMetrics.Count >= 2
            ? ParseTokenCount(_insights.SessionMetrics[1].Value)
            : 0;
        var percent = (int)Math.Clamp(inputTokens * 100.0 / approx, 0, 100);
        ContextBudgetPercent = percent;
    }

    private static int ParseTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text) || text == "—")
        {
            return 0;
        }
        var trimmed = text.Replace(",", "").Trim();
        if (trimmed.EndsWith("K", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(trimmed[..^1], out var k)) return (int)(k * 1000);
        }
        if (trimmed.EndsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(trimmed[..^1], out var m)) return (int)(m * 1_000_000);
        }
        return int.TryParse(trimmed, out var n) ? n : 0;
    }

    private void OnApprovalPresented(object? sender, ToolApprovalPresentedEventArgs args)
    {
        Activity.Add(new ActivityItemViewModel(
            "需要确认",
            args.Request.Preview.Summary,
            "等待"));
        StatusMessage = args.StatusMessage;
    }

    private void OnApprovalResolved(object? sender, ToolApprovalResolvedEventArgs args)
    {
        Activity.Add(new ActivityItemViewModel(
            args.Decision.IsApproved ? "已允许操作" : "已拒绝操作",
            args.Decision.IsApproved ? "AIChat 可以继续。" : args.Decision.Reason,
            args.Decision.IsApproved ? "已允许" : "已拒绝"));
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        StatusMessage = "正在读取本地状态...";
        _settings = await _repository.LoadSettingsAsync();
        _settingsHolder.Replace(_settings);
        // Apply the persisted theme now that we have the loaded settings.
        _theme.Apply(_settings.ThemePreference);
        ProviderSettingsService.Normalize(_settings, _settings.Temperature);
        AdvancedSettingsService.Normalize(_settings);
        ToolSettingsService.Normalize(_settings, _toolRegistry);

        var projects = (await _repository.LoadProjectsAsync()).ToList();
        var active = ProviderSettingsService.GetSelectedProvider(_settings);

        ActiveProvider = active is null ? "未配置模型" : active.Name;
        ActiveModel = active is null ? "配置模型后即可运行任务" : active.SelectedModelId;
        Readiness = active is not null && !string.IsNullOrWhiteSpace(active.ApiKey) ? "可运行" : "需要密钥";
        PrimaryActionText = Readiness == "可运行" ? "发送 ⌘↵" : "准备";
        AutoVerify = _settings.AutoVerifyAgentRuns;

        _sidebar.Refresh(projects);
        _conversationList.Refresh(_sidebar.CurrentProject);
        PopulateSafetyNotes();
        _provider.Refresh();
        _insights.PrepareContextPreview(DraftPrompt, _sidebar.CurrentProject, NoWriteMode);

        if (Activity.Count == 0)
        {
            ShowNewConversation();
        }

        StatusMessage = "已加载。";
    }

    [RelayCommand(CanExecute = nameof(CanSendTask))]
    private async Task SendTaskAsync()
    {
        var prompt = DraftPrompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Activity.Add(new ActivityItemViewModel("需要任务", "先描述你希望 AIChat 完成什么。", "等待"));
            StatusMessage = "请先输入任务。";
            return;
        }

        _insights.PrepareContextPreview(prompt, _sidebar.CurrentProject, NoWriteMode);

        var effectiveSettings = ProviderSettingsService.CreateEffectiveSettings(_settings, _settings.Temperature);
        var validation = ProviderConfigurationValidator.ValidateEffectiveSettings(effectiveSettings);
        if (!validation.IsValid || effectiveSettings is null)
        {
            var message = validation.Errors.FirstOrDefault()?.Message ?? "发送前需要配置模型密钥。";
            Activity.Add(new ActivityItemViewModel("需要配置模型", message, "已阻止"));
            StatusMessage = message;
            return;
        }

        if (_sidebar.CurrentProject is null || string.IsNullOrWhiteSpace(_sidebar.CurrentProject.Path))
        {
            Activity.Add(new ActivityItemViewModel("需要项目", "发送前请先选择或初始化项目。", "已阻止"));
            StatusMessage = "当前没有可运行的项目。";
            return;
        }

        await RunAgentTaskAsync(prompt, effectiveSettings);
    }

    private bool CanSendTask() => !IsRunning;

    partial void OnDraftPromptChanged(string value)
    {
        _insights.PrepareContextPreview(value, _sidebar.CurrentProject, NoWriteMode);
    }

    partial void OnNoWriteModeChanged(bool value)
    {
        _approvalViewModel.IsReadOnly = value;
        PopulateSafetyNotes();
        _insights.PrepareContextPreview(DraftPrompt, _sidebar.CurrentProject, NoWriteMode);
    }

    partial void OnAutoVerifyChanged(bool value)
    {
        _settings.AutoVerifyAgentRuns = value;
        PopulateSafetyNotes();
    }

    private void SeedEmptyState()
    {
        SafetyNotes.Add("写文件、命令、测试和 Git 操作前都会请求确认。");
        SafetyNotes.Add("只读模式会禁止修改类工具。");
        SafetyNotes.Add("开启验证后，修改完成会自动运行检查。");
        _insights.SeedEmptyState();
    }

    // PR-3: project list, selection, and add logic live in ProjectSidebarViewModel.
    // These two passthroughs keep the XAML code-behind talking to a single
    // view-model.
    public Task SelectProjectFromUiAsync(string projectId)
        => _sidebar.SelectProjectAsync(projectId);

    public Task AddProjectFromUiAsync(string projectPath)
        => _sidebar.AddProjectAsync(projectPath);

    public Task SelectConversationFromUiAsync(string conversationId)
    {
        _conversationList.SelectConversation(conversationId);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void SaveProvider() => _ = _provider.SaveProviderCommand.ExecuteAsync(null);

    [RelayCommand]
    private void TestProvider() => _ = _provider.TestProviderCommand.ExecuteAsync(null);

    // PR-2: handlers for events raised by ProviderConfigViewModel. They keep
    // the parent VM's display state in sync without re-architecting the
    // cross-VM contract.
    private void OnProviderSaved(object? sender, ProviderSavedEventArgs args)
    {
        if (args.ErrorMessage is not null)
        {
            StatusMessage = args.ErrorMessage;
            return;
        }

        ActiveProvider = args.ProviderName;
        ActiveModel = args.ModelId;
        Readiness = "可运行";
        PrimaryActionText = "发送 ⌘↵";
        StatusMessage = args.AlreadyExisted ? "已更新模型配置。" : "已保存模型配置。";
    }

    private void OnProviderTestStarted(object? sender, ProviderTestStartedEventArgs args)
    {
        IsRunning = true;
        StatusMessage = $"正在测试 {args.ProviderName}...";
        Activity.Add(new ActivityItemViewModel(
            "模型测试",
            $"正在连接 {args.ProviderName} ({args.ModelId})",
            "运行中"));
    }

    private void OnProviderTestCompleted(object? sender, ProviderTestCompletedEventArgs args)
    {
        IsRunning = false;
        if (args.Exception is not null)
        {
            Activity.Add(new ActivityItemViewModel("模型测试", $"测试失败：{args.Message}", "失败"));
            Readiness = "需检查";
            PrimaryActionText = "准备";
            StatusMessage = "模型连接失败。";
            return;
        }

        Activity.Add(new ActivityItemViewModel(
            "模型测试",
            args.Message,
            args.IsSuccess ? "通过" : "失败"));
        Readiness = args.IsSuccess ? "可运行" : "需检查";
        PrimaryActionText = args.IsSuccess ? "发送 ⌘↵" : "准备";
        StatusMessage = args.IsSuccess ? "模型连接正常。" : "模型连接失败。";
    }

    // PR-4: replaced by ConversationListViewModel.
    //
    // "New conversation" used to seed a placeholder activity item. With the
    // 1.0 Beta empty state that placeholder is redundant (the hero card
    // already explains how to start a task) and it was breaking the
    // HasConversation toggle — Activity.Count was always 1, so the empty
    // state never showed. Clear and let the XAML fall through to the
    // hero card.
    private void ShowNewConversation()
    {
        Activity.Clear();
    }

    // Loads a conversation's messages into the activity feed. Called by
    // the OnConversationSelected event handler. When the conversation is
    // null we show the "new conversation" prompt instead.
    private void ApplyConversationToActivity(Conversation? conversation)
    {
        if (conversation is null)
        {
            ShowNewConversation();
            return;
        }

        Activity.Clear();
        foreach (var message in conversation.Messages.OrderBy(message => message.CreatedAt))
        {
            var title = message.Role == ChatRole.User ? "你" : "AIChat";
            var status = message.CreatedAt.ToLocalTime().ToString("HH:mm");
            Activity.Add(new ActivityItemViewModel(title, message.Content, status));
        }

        if (Activity.Count == 0)
        {
            Activity.Add(new ActivityItemViewModel("AIChat", "这个对话还没有消息。", "空"));
        }
    }

    private void PopulateSafetyNotes()
    {
        SafetyNotes.Clear();
        SafetyNotes.Add(NoWriteMode ? "只读模式已开启。" : "写入文件前会请求确认。");
        SafetyNotes.Add("Shell、构建、测试和 Git 变更都需要确认。");
        SafetyNotes.Add(AutoVerify ? "修改完成后会自动验证。" : "自动验证暂未开启。");
    }

    private async Task RunPlainTaskAsync(string prompt, AppSettings effectiveSettings)
    {
        IsRunning = true;
        DraftPrompt = "";
        var userItem = new ActivityItemViewModel("你", prompt, "已发送");
        var assistantItem = new ActivityItemViewModel("AIChat", "正在连接模型...", "运行中");
        Activity.Add(userItem);
        Activity.Add(assistantItem);
        StatusMessage = "AIChat 正在思考...";

        var project = _sidebar.CurrentProject!;
        var conversation = new Conversation
        {
            ProjectId = project.Id,
            Title = prompt.Length > 80 ? prompt[..80] : prompt,
            UpdatedAt = DateTimeOffset.Now
        };
        var userMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRole.User,
            Content = prompt,
            CreatedAt = DateTimeOffset.Now
        };
        var assistantMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRole.Assistant,
            Content = "",
            CreatedAt = DateTimeOffset.Now
        };
        conversation.Messages.Add(userMessage);
        conversation.Messages.Add(assistantMessage);

        try
        {
            var requestFactory = new AgentRequestFactory(
                new ConversationContextBuilder(
                    new TokenizerContextEstimator(),
                    new SystemPromptBuilder()));
            var requestBuild = requestFactory.Build(new AgentRequestBuildRequest
            {
                Conversation = conversation,
                AssistantMessageId = assistantMessage.Id,
                EffectiveSettings = effectiveSettings,
                RuntimeSettings = RuntimeSettingsBuilder.Plain(_settings),
                ProjectName = project.Name,
                ProjectPath = project.Path,
                ProjectLoadSnapshot = BuildProjectSnapshot(project),
                PinnedContextItems = project.PinnedContext,
                InputArtifacts = project.InputArtifacts,
                MemoryEntries = project.Memories,
                ProjectToolPermissionModes = project.ProjectToolPermissionModes,
                VerificationCommands = project.VerificationCommands
            });

            assistantItem.Detail = "";
            var receivedContent = false;
            await foreach (var delta in _chatService.SendAsync(requestBuild.ChatRequest, effectiveSettings))
            {
                if (string.IsNullOrEmpty(delta.Content))
                {
                    continue;
                }

                receivedContent = true;
                assistantMessage.Content += delta.Content;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    assistantItem.Detail += delta.Content;
                    StatusMessage = "正在接收回复...";
                });
            }

            if (!receivedContent)
            {
                assistantItem.Detail = "模型没有返回可见文本。";
            }

            assistantItem.Status = "完成";
            conversation.UpdatedAt = DateTimeOffset.Now;
            project.Conversations.Add(conversation);
            project.UpdatedAt = DateTimeOffset.Now;
            await SaveProjectsAsync();
            _conversationList.Refresh(project, conversation.Id);
            StatusMessage = "完成。";
        }
        catch (Exception ex)
        {
            assistantItem.Status = "失败";
            assistantItem.Detail = $"请求失败：{ex.Message}";
            StatusMessage = "请求失败。";
        }
        finally
        {
            IsRunning = false;
        }
    }

    // PR-6: Approve / Reject commands live on ToolApprovalViewModel.

    private async Task RunAgentTaskAsync(string prompt, AppSettings effectiveSettings)
    {
        IsRunning = true;
        DraftPrompt = "";
        var userItem = new ActivityItemViewModel("你", prompt, "已发送");
        var assistantItem = new ActivityItemViewModel("AIChat", NoWriteMode ? "正在以只读模式启动..." : "正在启动任务...", "运行中");
        Activity.Add(userItem);
        Activity.Add(assistantItem);
        StatusMessage = "AIChat 正在读取上下文...";

        var project = _sidebar.CurrentProject!;
        var conversation = new Conversation
        {
            ProjectId = project.Id,
            Title = prompt.Length > 80 ? prompt[..80] : prompt,
            UpdatedAt = DateTimeOffset.Now
        };
        var userMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRole.User,
            Content = prompt,
            CreatedAt = DateTimeOffset.Now
        };
        var assistantMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRole.Assistant,
            Content = "",
            CreatedAt = DateTimeOffset.Now
        };
        conversation.Messages.Add(userMessage);
        conversation.Messages.Add(assistantMessage);

        try
        {
            var runtimeSettings = NoWriteMode
                ? RuntimeSettingsBuilder.ReadOnly(_settings, _toolRegistry)
                : RuntimeSettingsBuilder.Gui(_settings, _toolRegistry);
            var requestFactory = new AgentRequestFactory(
                new ConversationContextBuilder(
                    new TokenizerContextEstimator(),
                    new SystemPromptBuilder()));
            var requestBuild = requestFactory.Build(new AgentRequestBuildRequest
            {
                Conversation = conversation,
                AssistantMessageId = assistantMessage.Id,
                EffectiveSettings = effectiveSettings,
                RuntimeSettings = runtimeSettings,
                ProjectName = project.Name,
                ProjectPath = project.Path,
                ProjectLoadSnapshot = BuildProjectSnapshot(project),
                PinnedContextItems = project.PinnedContext,
                InputArtifacts = project.InputArtifacts,
                MemoryEntries = project.Memories,
                ProjectToolPermissionModes = project.ProjectToolPermissionModes,
                VerificationCommands = project.VerificationCommands,
                RequestToolApprovalAsync = _approval.RequestApprovalAsync
            });

            _insights.BeginRun(
                prompt,
                requestBuild.ContextPack?.EstimatedTokens ?? 0,
                project.VerificationCommands.Count);

            var harness = new AgentHarness(
                new AgentRunner(_chatService, new AgentToolCatalog(_toolRegistry.All)));
            assistantItem.Detail = "";
            await foreach (var agentEvent in harness.RunAsync(new AgentHarnessRunRequest
                           {
                               Conversation = conversation,
                               UserMessageId = userMessage.Id,
                               AssistantMessageId = assistantMessage.Id,
                               Goal = prompt,
                               ChatRequest = requestBuild.ChatRequest,
                               Settings = effectiveSettings,
                               ContextPack = requestBuild.ContextPack,
                               Context = requestBuild.AgentContext
                           }))
            {
                await ApplyAgentEventAsync(agentEvent, assistantItem, assistantMessage);
            }

            if (string.IsNullOrWhiteSpace(assistantItem.Detail))
            {
                assistantItem.Detail = "本次运行已结束，但没有可显示的文本。";
            }

            assistantItem.Status = "完成";
            _insights.UpdateMetrics(conversation.AgentRuns.LastOrDefault(), assistantMessage.Content, _sidebar.CurrentProject?.VerificationCommands.Count ?? 0);
            conversation.UpdatedAt = DateTimeOffset.Now;
            project.Conversations.Add(conversation);
            project.UpdatedAt = DateTimeOffset.Now;
            await SaveProjectsAsync();
            _conversationList.Refresh(project, conversation.Id);
            StatusMessage = "完成。";
        }
        catch (Exception ex)
        {
            assistantItem.Status = "失败";
            assistantItem.Detail = $"请求失败：{ex.Message}";
            StatusMessage = "请求失败。";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task ApplyAgentEventAsync(
        AgentHarnessEvent agentEvent,
        ActivityItemViewModel assistantItem,
        ChatMessage assistantMessage)
    {
        switch (agentEvent.Type)
        {
            case AgentHarnessEventType.PhaseChanged:
                if (!string.IsNullOrWhiteSpace(agentEvent.PhaseTransition?.Summary))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusMessage = agentEvent.PhaseTransition.Summary;
                    });
                }

                break;
            case AgentHarnessEventType.ToolCall:
                if (!string.IsNullOrWhiteSpace(agentEvent.ToolCall?.Name))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Activity.Add(new ActivityItemViewModel(
                            "正在读取",
                            FriendlyToolSummary(agentEvent.ToolCall.Name),
                            "工具"));
                        _insights.UpdateMetrics(agentEvent.Run, assistantMessage.Content, _sidebar.CurrentProject?.VerificationCommands.Count ?? 0);
                    });
                }

                break;
            case AgentHarnessEventType.ToolApprovalRejected:
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Activity.Add(new ActivityItemViewModel(
                        "已跳过操作",
                        agentEvent.ToolPreview?.Summary ?? "此操作需要确认后才能执行。",
                        "已阻止"));
                });
                break;
            case AgentHarnessEventType.ToolResult:
                if (agentEvent.ToolResult?.IsError == true)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Activity.Add(new ActivityItemViewModel(
                            "工具问题",
                            agentEvent.ToolResult.Content,
                            "需查看"));
                    });
                }

                break;
            case AgentHarnessEventType.ContentDelta:
                if (!string.IsNullOrEmpty(agentEvent.Content))
                {
                    assistantMessage.Content += agentEvent.Content;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        assistantItem.Detail += agentEvent.Content;
                        StatusMessage = "正在接收回复...";
                        _insights.UpdateMetrics(agentEvent.Run, assistantMessage.Content, _sidebar.CurrentProject?.VerificationCommands.Count ?? 0);
                    });
                }

                break;
            case AgentHarnessEventType.RunCompleted:
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = agentEvent.Run?.CompletionReason is { Length: > 0 } reason ? reason : "运行完成。";
                    _insights.UpdateMetrics(agentEvent.Run, assistantMessage.Content, _sidebar.CurrentProject?.VerificationCommands.Count ?? 0);
                });
                break;
        }
    }

    private static string FriendlyToolSummary(string toolName)
    {
        return toolName switch
        {
            "list_files" => "正在列出项目文件",
            "read_file" => "正在读取文件",
            "search_text" => "正在搜索项目",
            "read_input_artifact" => "正在读取输入资料",
            "update_plan" => "正在更新任务计划",
            _ => $"正在使用 {toolName}"
        };
    }

    private async Task SaveProjectsAsync()
    {
        var projects = (await _repository.LoadProjectsAsync()).ToList();
        var index = projects.FindIndex(project => project.Id == _sidebar.CurrentProject?.Id);
        if (index >= 0)
        {
            projects[index] = _sidebar.CurrentProject!;
        }
        else if (_sidebar.CurrentProject is not null)
        {
            projects.Add(_sidebar.CurrentProject);
        }

        await _repository.SaveProjectsAsync(projects);
    }

    private static string BuildProjectSnapshot(ProjectWorkspace project)
    {
        var snapshot = ProjectLoadSnapshotBuilder.Build(project);
        return string.Join(Environment.NewLine, [
            snapshot.HealthText,
            snapshot.ProfileText,
            snapshot.ActivityText,
            snapshot.RecommendationText
        ]);
    }
}

public sealed record ProjectCardViewModel(string Id, string Name, string Path);

public sealed partial class ActivityItemViewModel(string title, string detail, string status) : ViewModelBase
{
    public string Title { get; } = title;
    public HorizontalAlignment BubbleAlignment { get; } = GetAlignment(title);
    public IBrush BubbleBackground { get; } = GetBackgroundBrush(title);
    public IBrush TextForeground { get; } = GetForegroundBrush(title);
    public double BubbleMaxWidth { get; } = title == "你" ? 620 : 760;

    [ObservableProperty]
    private string detail = detail;

    [ObservableProperty]
    private string status = status;

    // The "thinking" state is: an assistant bubble that has not yet received
    // any content from the model. The XAML renders three animated dots
    // instead of the detail markdown, so the user always knows the run is
    // in flight.
    public bool IsThinking => Title == "AIChat" && string.IsNullOrEmpty(Detail) && Status == "运行中";

    // Bubble classification: the 1.0 Beta redesign needs three distinct
    // bubble styles (user right-aligned, AI with avatar, system centered),
    // and the XAML can't switch on Title in a binding. These flags make
    // the templates declarative.
    public bool IsUserBubble => Title == "你";
    public bool IsAssistantBubble => Title == "AIChat";
    public bool IsSystemBubble => !IsUserBubble && !IsAssistantBubble;

    partial void OnDetailChanged(string value) => OnPropertyChanged(nameof(IsThinking));
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(IsThinking));

    private static HorizontalAlignment GetAlignment(string title)
    {
        return title switch
        {
            "你" => HorizontalAlignment.Right,
            "AIChat" => HorizontalAlignment.Left,
            _ => HorizontalAlignment.Center
        };
    }

    // Bubble palettes route through design tokens so they flip in dark mode.
    // User bubbles use the accent + on-accent text; assistant bubbles use the
    // surface + body text; system bubbles use the info background.
    private static IBrush GetBackgroundBrush(string title)
    {
        return title switch
        {
            "你" => TokenBrush("AccentBrush"),
            "AIChat" => TokenBrush("SurfaceBrush"),
            _ => TokenBrush("InfoBgBrush")
        };
    }

    // v1 bug AP-3 fix: user bubble is now soft-accent (8% teal) on a white
    // surface, NOT solid accent. The previous TextOnAccentBrush (white) on
    // a near-white wash was effectively invisible in light mode. Use the
    // primary text brush so the user's words read correctly.
    private static IBrush GetForegroundBrush(string title)
    {
        return title == "你"
            ? TokenBrush("TextBrush")
            : TokenBrush("TextBrush");
    }
}

public sealed record ProviderCardViewModel(string Name, string DefaultModel, string Status);
public sealed record ProviderTemplateViewModel(string Id, string Name, string DefaultBaseUrl, string DefaultModel);
