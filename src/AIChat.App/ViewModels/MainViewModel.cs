using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using AIChat.App.Controls;
using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Context;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.Application.Agents;
using AIChat.Application.Context;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Domain.Context;

namespace AIChat.App.ViewModels;

// Main application state machine. This ViewModel coordinates UI state,
// persistence, context estimation, and model calls without depending on WPF
// controls directly.
public sealed class MainViewModel : ObservableObject
{
    private const double AgentDefaultTemperature = 0.3;

    // Used for request/response snapshots in the call-detail inspector.
    private static readonly JsonSerializerOptions DetailJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IAppRepository _repository;
    private readonly IChatCompletionService _chatService;
    private readonly IContextEstimator _contextEstimator;
    private readonly ConversationContextBuilder _contextBuilder;
    private readonly WorkspaceChangeService _workspaceChangeService;
    private AgentHarness? _agentHarness;
    private AgentToolCatalog? _toolCatalog;
    private ProjectViewModel? _selectedProject;
    private ConversationViewModel? _selectedConversation;
    private AppSettings _settings = new();
    private string _draftMessage = "";
    private bool _isSending;
    private string _statusText = "就绪";
    private ContextUsage _contextUsage = new() { ModelLimit = 128_000, ConversationLimit = 64_000 };
    private CancellationTokenSource? _sendCts;
    private bool _isSettingsOpen;
    private bool _isCallDetailsOpen;
    private bool _isNewProviderApiKeyVisible;
    private bool _isTestingProviderConnection;
    private PendingToolApprovalViewModel? _pendingToolApproval;
    private string _newProviderApiKey = "";
    private string _conversationSearchText = "";
    private ConversationViewModel? _callDetailsConversation;
    private LlmCallDetailViewModel? _selectedCallDetail;
    private string _selectedCallRequestJson = "请选择左侧调用记录。";
    private string _selectedCallResponseJson = "请选择左侧调用记录。";
    private bool _showSelectedCallRawEvents;
    private WorkspaceChangeViewModel? _selectedWorkspaceChange;
    private string _workspaceBranch = "";
    private string _workspaceStatusText = "尚未刷新";
    private string _workspaceDiffText = "选择一个变更文件查看 diff。";
    private bool _isRefreshingWorkspaceChanges;
    // Guards against older async JSON loads overwriting a newer selection.
    private int _callDetailLoadVersion;
    private int _workspaceDiffLoadVersion;

    public MainViewModel(
        IAppRepository repository,
        IChatCompletionService chatService,
        IContextEstimator contextEstimator,
        ConversationContextBuilder contextBuilder,
        WorkspaceChangeService workspaceChangeService)
    {
        _repository = repository;
        _chatService = chatService;
        _contextEstimator = contextEstimator;
        _contextBuilder = contextBuilder;
        _workspaceChangeService = workspaceChangeService;
        // Commands are the bridge from XAML buttons/menu items to ViewModel methods.
        NewChatCommand = new RelayCommand(_ => NewChat(), _ => SelectedProject is not null && !IsSending);
        SendCommand = new RelayCommand(async _ => await SendAsync(), _ => CanSend);
        SelectProjectCommand = new RelayCommand(parameter => SelectProject((ProjectViewModel)parameter!));
        SelectConversationCommand = new RelayCommand(parameter => SelectConversation((ConversationViewModel)parameter!));
        OpenSettingsCommand = new RelayCommand(_ => IsSettingsOpen = true);
        CloseSettingsCommand = new RelayCommand(_ => IsSettingsOpen = false);
        SaveSettingsCommand = new RelayCommand(async _ =>
        {
            await SaveSettingsAsync(Settings);
            IsSettingsOpen = false;
        });
        StopCommand = new RelayCommand(_ => _sendCts?.Cancel(), _ => IsSending);
        CopyMessageCommand = new RelayCommand(parameter => CopyMessage((ChatMessageViewModel)parameter!));
        CopyConversationTitleCommand = new RelayCommand(parameter => CopyConversationTitle((ConversationViewModel)parameter!));
        RenameConversationCommand = new RelayCommand(async parameter => await RenameConversationAsync((ConversationViewModel)parameter!), parameter => parameter is ConversationViewModel);
        DeleteConversationCommand = new RelayCommand(async parameter => await DeleteConversationAsync((ConversationViewModel)parameter!), parameter => parameter is ConversationViewModel);
        OpenCallDetailsCommand = new RelayCommand(parameter => OpenCallDetails((ConversationViewModel)parameter!), parameter => parameter is ConversationViewModel);
        CloseCallDetailsCommand = new RelayCommand(_ => IsCallDetailsOpen = false);
        AddConfiguredProviderCommand = new RelayCommand(async _ => await AddConfiguredProviderAsync(), _ => !string.IsNullOrWhiteSpace(NewProviderApiKey));
        RemoveConfiguredProviderCommand = new RelayCommand(async _ => await RemoveConfiguredProviderAsync(), _ => SelectedConfiguredProvider is not null);
        ToggleNewProviderApiKeyVisibilityCommand = new RelayCommand(_ => IsNewProviderApiKeyVisible = !IsNewProviderApiKeyVisible);
        TestProviderConnectionCommand = new RelayCommand(async _ => await TestProviderConnectionAsync(), _ => !IsTestingProviderConnection && !string.IsNullOrWhiteSpace(NewProviderApiKey));
        RefreshWorkspaceChangesCommand = new RelayCommand(async _ => await RefreshWorkspaceChangesAsync(), _ => SelectedProject is not null && !IsRefreshingWorkspaceChanges);
        ApproveToolCommand = new RelayCommand(_ => ResolvePendingToolApproval(allow: true, allowForSession: false), _ => PendingToolApproval is not null);
        ApproveToolForSessionCommand = new RelayCommand(_ => ResolvePendingToolApproval(allow: true, allowForSession: true), _ => PendingToolApproval is not null);
        RejectToolCommand = new RelayCommand(_ => ResolvePendingToolApproval(allow: false, allowForSession: false), _ => PendingToolApproval is not null);
    }

    public ObservableCollection<ProjectViewModel> Projects { get; } = [];
    public ObservableCollection<ToolOptionViewModel> ToolOptions { get; } = [];
    public ObservableCollection<ModelParameterOptionViewModel> ModelParameterOptions { get; } = [];
    public ObservableCollection<WorkspaceChangeViewModel> WorkspaceChanges { get; } = [];
    public IReadOnlyList<SelectionOptionViewModel> ToolPermissionModeOptions { get; } =
    [
        new() { Id = nameof(ToolPermissionMode.AutoReadOnly), Name = "只读自动" },
        new() { Id = nameof(ToolPermissionMode.ConfirmEachTime), Name = "每次确认" },
        new() { Id = nameof(ToolPermissionMode.AllowForSession), Name = "本会话允许" },
        new() { Id = nameof(ToolPermissionMode.Disabled), Name = "关闭" }
    ];
    public IReadOnlyList<LlmProviderInfo> ProviderOptions { get; } = ChatProviderCatalog.All;
    public RelayCommand NewChatCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand SelectProjectCommand { get; }
    public RelayCommand SelectConversationCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand CloseSettingsCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand CopyMessageCommand { get; }
    public RelayCommand CopyConversationTitleCommand { get; }
    public RelayCommand RenameConversationCommand { get; }
    public RelayCommand DeleteConversationCommand { get; }
    public RelayCommand OpenCallDetailsCommand { get; }
    public RelayCommand CloseCallDetailsCommand { get; }
    public RelayCommand AddConfiguredProviderCommand { get; }
    public RelayCommand RemoveConfiguredProviderCommand { get; }
    public RelayCommand ToggleNewProviderApiKeyVisibilityCommand { get; }
    public RelayCommand TestProviderConnectionCommand { get; }
    public RelayCommand RefreshWorkspaceChangesCommand { get; }
    public RelayCommand ApproveToolCommand { get; }
    public RelayCommand ApproveToolForSessionCommand { get; }
    public RelayCommand RejectToolCommand { get; }

    public PendingToolApprovalViewModel? PendingToolApproval
    {
        get => _pendingToolApproval;
        private set
        {
            if (SetProperty(ref _pendingToolApproval, value))
            {
                OnPropertyChanged(nameof(HasPendingToolApproval));
                ApproveToolCommand.RaiseCanExecuteChanged();
                ApproveToolForSessionCommand.RaiseCanExecuteChanged();
                RejectToolCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasPendingToolApproval => PendingToolApproval is not null;

    public AppSettings Settings
    {
        get => _settings;
        set
        {
            if (SetProperty(ref _settings, value))
            {
                // Many display properties are derived from Settings, so a settings
                // replacement must notify all dependent bindings.
                UpdateContextUsage();
                OnPropertyChanged(nameof(ModelName));
                OnPropertyChanged(nameof(HasApiKey));
                OnPropertyChanged(nameof(ConfiguredProviders));
                OnPropertyChanged(nameof(SelectedConfiguredProvider));
                OnPropertyChanged(nameof(ActiveModelOptions));
            }
        }
    }

    public ProjectViewModel? SelectedProject
    {
        get => _selectedProject;
        private set
        {
            if (SetProperty(ref _selectedProject, value))
            {
                OnPropertyChanged(nameof(CurrentProjectName));
                NewChatCommand.RaiseCanExecuteChanged();
                RefreshWorkspaceChangesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ConversationViewModel? SelectedConversation
    {
        get => _selectedConversation;
        private set
        {
            if (SetProperty(ref _selectedConversation, value))
            {
                OnPropertyChanged(nameof(CurrentConversationTitle));
                OnPropertyChanged(nameof(Messages));
                OnPropertyChanged(nameof(HasMessages));
                UpdateContextUsage();
            }
        }
    }

    public ObservableCollection<ChatMessageViewModel>? Messages => SelectedConversation?.Messages;
    public bool HasMessages => Messages?.Count > 0;
    public string CurrentProjectName => SelectedProject?.Name ?? "未选择项目";
    public string CurrentConversationTitle => SelectedConversation?.Title ?? "新对话";
    public string ModelName => SelectedConfiguredProvider is null
        ? "未配置模型"
        : $"{SelectedConfiguredProvider.Name} · {SelectedConfiguredProvider.SelectedModelId}";
    public IReadOnlyList<ConfiguredLlmProvider> ConfiguredProviders => Settings.ConfiguredProviders;
    public ConfiguredLlmProvider? SelectedConfiguredProvider =>
        Settings.ConfiguredProviders.FirstOrDefault(provider => provider.Id == Settings.ActiveConfiguredProviderId) ??
        Settings.ConfiguredProviders.FirstOrDefault();
    public IReadOnlyList<LlmModelInfo> ActiveModelOptions => SelectedConfiguredProvider is null
        ? []
        : ChatProviderCatalog.Resolve(SelectedConfiguredProvider.TemplateId).Models;
    public bool HasModelParameterOptions => ModelParameterOptions.Count > 0;
    public string ActiveModelCapabilitySummary
    {
        get
        {
            var configured = SelectedConfiguredProvider;
            if (configured is null)
            {
                return "未选择模型";
            }

            var model = ChatProviderCatalog.ResolveModel(configured.TemplateId, configured.SelectedModelId);
            return string.IsNullOrWhiteSpace(model.CapabilityLabel)
                ? "标准聊天能力"
                : model.CapabilityLabel;
        }
    }
    public bool HasApiKey => SelectedConfiguredProvider is not null && !string.IsNullOrWhiteSpace(SelectedConfiguredProvider.ApiKey);
    public bool HasWorkspaceChanges => WorkspaceChanges.Count > 0;
    public string WorkspaceBranch
    {
        get => _workspaceBranch;
        private set => SetProperty(ref _workspaceBranch, value);
    }
    public string WorkspaceStatusText
    {
        get => _workspaceStatusText;
        private set => SetProperty(ref _workspaceStatusText, value);
    }
    public string WorkspaceDiffText
    {
        get => _workspaceDiffText;
        private set => SetProperty(ref _workspaceDiffText, value);
    }
    public bool IsRefreshingWorkspaceChanges
    {
        get => _isRefreshingWorkspaceChanges;
        private set
        {
            if (SetProperty(ref _isRefreshingWorkspaceChanges, value))
            {
                RefreshWorkspaceChangesCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public WorkspaceChangeViewModel? SelectedWorkspaceChange
    {
        get => _selectedWorkspaceChange;
        set
        {
            if (SetProperty(ref _selectedWorkspaceChange, value))
            {
                _ = LoadSelectedWorkspaceDiffAsync();
            }
        }
    }
    public string SelectedProviderId
    {
        get => Settings.ProviderId;
        set
        {
            if (Settings.ProviderId == value)
            {
                return;
            }

            var provider = ChatProviderCatalog.Resolve(value);
            // Provider template changes reset protocol/base URL/model to known
            // catalog values before persisting.
            Settings.ProviderId = provider.Id;
            Settings.ProtocolId = provider.ProtocolId;
            Settings.ProviderName = provider.Name;
            Settings.BaseUrl = provider.DefaultBaseUrl;
            Settings.Model = provider.DefaultModel;
            Settings.ModelContextLimit = provider.DefaultContextLimit;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(ModelName));
            OnPropertyChanged(nameof(SelectedActiveModelId));
            OnPropertyChanged(nameof(ActiveModelOptions));
            RebuildModelParameterOptions();
            UpdateContextUsage();
            _ = PersistSettingsQuietlyAsync();
        }
    }

    public string SelectedConfiguredProviderId
    {
        get => Settings.ActiveConfiguredProviderId;
        set
        {
            if (Settings.ActiveConfiguredProviderId == value)
            {
                return;
            }

            Settings.ActiveConfiguredProviderId = value;
            // Copy the selected saved provider into the flat Settings fields used
            // by the rest of the app.
            ApplySelectedConfiguredProvider();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(SelectedConfiguredProvider));
            OnPropertyChanged(nameof(ModelName));
            OnPropertyChanged(nameof(SelectedActiveModelId));
            OnPropertyChanged(nameof(ActiveModelOptions));
            RebuildModelParameterOptions();
            UpdateContextUsage();
            _ = PersistSettingsQuietlyAsync();
        }
    }

    public string SelectedActiveModelId
    {
        get => SelectedConfiguredProvider?.SelectedModelId ?? "";
        set
        {
            var configured = SelectedConfiguredProvider;
            if (configured is null || configured.SelectedModelId == value)
            {
                return;
            }

            var model = ChatProviderCatalog.ResolveModel(configured.TemplateId, value);
            // Model selection also changes the context limit.
            configured.SelectedModelId = model.Id;
            configured.ModelParameters = NormalizeModelParameterValues(configured.TemplateId, model.Id, configured.ModelParameters);
            ApplySelectedConfiguredProvider();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(ModelName));
            RebuildModelParameterOptions();
            UpdateContextUsage();
            _ = PersistSettingsQuietlyAsync();
        }
    }

    public string NewProviderApiKey
    {
        get => _newProviderApiKey;
        set
        {
            if (SetProperty(ref _newProviderApiKey, value))
            {
                AddConfiguredProviderCommand.RaiseCanExecuteChanged();
                TestProviderConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNewProviderApiKeyVisible
    {
        get => _isNewProviderApiKeyVisible;
        set => SetProperty(ref _isNewProviderApiKeyVisible, value);
    }

    public bool IsTestingProviderConnection
    {
        get => _isTestingProviderConnection;
        private set
        {
            if (SetProperty(ref _isTestingProviderConnection, value))
            {
                TestProviderConnectionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConversationSearchText
    {
        get => _conversationSearchText;
        set
        {
            if (SetProperty(ref _conversationSearchText, value))
            {
                ApplyConversationFilters();
            }
        }
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
    }

    public bool IsCallDetailsOpen
    {
        get => _isCallDetailsOpen;
        set => SetProperty(ref _isCallDetailsOpen, value);
    }

    public ObservableCollection<LlmCallDetailViewModel>? CurrentCallDetails => _callDetailsConversation?.CallDetails;
    public string CallDetailsTitle => _callDetailsConversation is null
        ? "调用详情"
        : $"{_callDetailsConversation.Title} · 调用详情";

    public LlmCallDetailViewModel? SelectedCallDetail
    {
        get => _selectedCallDetail;
        set
        {
            if (SetProperty(ref _selectedCallDetail, value))
            {
                _ = LoadSelectedCallDetailJsonAsync(value);
            }
        }
    }
    public string SelectedCallRequestJson
    {
        get => _selectedCallRequestJson;
        private set => SetProperty(ref _selectedCallRequestJson, value);
    }

    public string SelectedCallResponseJson
    {
        get => _selectedCallResponseJson;
        private set => SetProperty(ref _selectedCallResponseJson, value);
    }

    public bool ShowSelectedCallRawEvents
    {
        get => _showSelectedCallRawEvents;
        set
        {
            if (SetProperty(ref _showSelectedCallRawEvents, value))
            {
                _ = LoadSelectedCallDetailJsonAsync(SelectedCallDetail);
            }
        }
    }

    public string DraftMessage
    {
        get => _draftMessage;
        set
        {
            if (SetProperty(ref _draftMessage, value))
            {
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (SetProperty(ref _isSending, value))
            {
                SendCommand.RaiseCanExecuteChanged();
                NewChatCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ContextUsage ContextUsage
    {
        get => _contextUsage;
        private set
        {
            if (SetProperty(ref _contextUsage, value))
            {
                // The ring, tooltip, labels, and warnings are all projections of
                // the same ContextUsage value.
                OnPropertyChanged(nameof(ContextPercent));
                OnPropertyChanged(nameof(ConversationUsagePercent));
                OnPropertyChanged(nameof(ConversationRemainingPercent));
                OnPropertyChanged(nameof(ContextWindowSummary));
                OnPropertyChanged(nameof(ContextTokenSummary));
                OnPropertyChanged(nameof(ContextTooltip));
                OnPropertyChanged(nameof(ContextLabel));
                OnPropertyChanged(nameof(ContextCompressionHint));
            }
        }
    }

    public double ContextPercent => ContextUsage.Ratio * 100;
    public double ConversationUsagePercent => ContextUsage.ConversationLimit <= 0
        ? 0
        : Math.Clamp((double)ContextUsage.CurrentTokens / ContextUsage.ConversationLimit * 100, 0, 100);
    public double ConversationRemainingPercent => Math.Max(0, 100 - ConversationUsagePercent);
    public string ContextLabel => $"{ContextUsage.CurrentTokens / 1000.0:0.#}K";
    public string ContextWindowSummary => $"{ConversationUsagePercent:0.#}% 已用（剩余 {ConversationRemainingPercent:0.#}%）";
    public string ContextTokenSummary => $"已用 {ContextUsage.CurrentTokens:N0} tokens，共 {ContextUsage.ConversationLimit:N0}";
    public string ContextCompressionHint => ConversationUsagePercent >= 85
        ? "接近上限时将自动压缩背景信息"
        : "AIChat 会自动保留可用背景信息";
    public string ContextTooltip =>
        $"背景信息窗口：{ConversationUsagePercent:0.#}% 已用（剩余 {ConversationRemainingPercent:0.#}%）\n" +
        $"已用 {ContextUsage.CurrentTokens:N0} tokens，共 {ContextUsage.ConversationLimit:N0}\n" +
        ContextCompressionHint;

    private bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(DraftMessage) && SelectedProject is not null;

    public void ConfigureAgent(AgentHarness agentHarness, AgentToolCatalog toolCatalog)
    {
        _agentHarness = agentHarness;
        _toolCatalog = toolCatalog;
        RebuildToolOptions();
    }

    public async Task InitializeAsync()
    {
        // Startup sequence: load settings, normalize old values, load projects,
        // then select the default project/conversation for the UI.
        Settings = await _repository.LoadSettingsAsync();
        NormalizeProviderSettings();
        NormalizeToolSettings();
        NormalizeModelParameters();
        RebuildToolOptions();
        RebuildModelParameterOptions();
        await _repository.SaveSettingsAsync(Settings);
        var projects = await _repository.LoadProjectsAsync();
        Projects.Clear();
        foreach (var project in projects)
        {
            Projects.Add(new ProjectViewModel(project));
        }

        SelectProject(Projects.FirstOrDefault(project => project.Name == "AIChat") ?? Projects.FirstOrDefault());
        await RefreshWorkspaceChangesAsync();
        StatusText = HasApiKey ? "可直接对话" : "请先在设置中添加模型提供商";
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        // Settings can be saved from the modal; normalization keeps persisted data
        // aligned with the current provider catalog.
        Settings = settings;
        SyncToolOptionsToSettings();
        SyncModelParameterOptionsToSettings();
        NormalizeProviderSettings();
        NormalizeToolSettings();
        NormalizeModelParameters();
        RebuildToolOptions();
        RebuildModelParameterOptions();
        await _repository.SaveSettingsAsync(Settings);
        UpdateContextUsage();
        OnPropertyChanged(nameof(ModelName));
        OnPropertyChanged(nameof(HasApiKey));
        StatusText = HasApiKey ? "设置已保存，可以对话" : "设置已保存，仍缺少 API Key";
    }

    private void SelectProject(ProjectViewModel? project)
    {
        if (project is null)
        {
            return;
        }

        foreach (var item in Projects)
        {
            // Selection state is kept on each item because the sidebar binds to it.
            item.IsSelected = item == project;
        }

        SelectedProject = project;
        var conversation = project.Conversations.FirstOrDefault();
        if (conversation is not null)
        {
            SelectConversation(conversation);
        }
        else
        {
            SelectedConversation = null;
        }

        _ = RefreshWorkspaceChangesAsync();
    }

    private void SelectConversation(ConversationViewModel conversation)
    {
        if (SelectedProject is null)
        {
            return;
        }

        foreach (var project in Projects)
        {
            foreach (var item in project.Conversations)
            {
                item.IsSelected = item == conversation;
            }
        }

        SelectedConversation = conversation;
    }

    private async void NewChat()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var conversation = SelectedProject.FindUnstartedConversation();
        // Reuse an empty conversation instead of creating many blank rows.
        if (conversation is null)
        {
            conversation = SelectedProject.CreateConversation();
        }

        if (!string.IsNullOrWhiteSpace(ConversationSearchText))
        {
            ConversationSearchText = "";
        }

        SelectConversation(conversation);
        await SaveProjectsAsync();
    }

    private void CopyMessage(ChatMessageViewModel message)
    {
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            System.Windows.Clipboard.SetText(message.Content);
            StatusText = "消息已复制";
        }
    }

    private void CopyConversationTitle(ConversationViewModel conversation)
    {
        System.Windows.Clipboard.SetText(conversation.Title);
        StatusText = "标题已复制";
    }

    private async Task RenameConversationAsync(ConversationViewModel conversation)
    {
        var title = TextPromptDialog.Show(System.Windows.Application.Current.MainWindow, "重命名会话", conversation.Title);
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        conversation.Rename(title);
        OnPropertyChanged(nameof(CurrentConversationTitle));
        ApplyConversationFilters();
        await SaveProjectsAsync();
        StatusText = "会话已重命名";
    }

    private void OpenCallDetails(ConversationViewModel conversation)
    {
        // The inspector is tied to one conversation at a time and reads its saved
        // request/response snapshots.
        _callDetailsConversation = conversation;
        SelectedCallDetail = null;
        ShowSelectedCallRawEvents = false;
        SelectedCallRequestJson = conversation.CallDetails.Count == 0 ? "暂无调用记录。" : "请选择左侧调用记录。";
        SelectedCallResponseJson = conversation.CallDetails.Count == 0 ? "暂无调用记录。" : "请选择左侧调用记录。";
        OnPropertyChanged(nameof(CurrentCallDetails));
        OnPropertyChanged(nameof(CallDetailsTitle));
        IsCallDetailsOpen = true;
    }

    private async Task DeleteConversationAsync(ConversationViewModel conversation)
    {
        var project = Projects.FirstOrDefault(item => item.Conversations.Contains(conversation));
        if (project is null || project.Conversations.Count <= 1)
        {
            StatusText = "至少保留一个对话";
            return;
        }

        project.Conversations.Remove(conversation);
        project.VisibleConversations.Remove(conversation);
        project.Project.Conversations.Remove(conversation.Conversation);
        if (SelectedConversation == conversation)
        {
            SelectConversation(project.Conversations.First());
        }

        await SaveProjectsAsync();
        StatusText = "对话已删除";
    }

    private async Task RefreshWorkspaceChangesAsync()
    {
        if (SelectedProject is null || IsRefreshingWorkspaceChanges)
        {
            return;
        }

        IsRefreshingWorkspaceChanges = true;
        try
        {
            var changeSet = await _workspaceChangeService.GetChangesAsync(SelectedProject.Path);
            WorkspaceChanges.Clear();
            foreach (var change in changeSet.Changes)
            {
                WorkspaceChanges.Add(new WorkspaceChangeViewModel(change));
            }

            WorkspaceBranch = changeSet.Branch;
            WorkspaceStatusText = changeSet.HasChanges
                ? $"{changeSet.Changes.Count} 个变更{(changeSet.IsTruncated ? "，列表已截断" : "")}"
                : "工作区干净";
            SelectedWorkspaceChange = WorkspaceChanges.FirstOrDefault();
            if (SelectedWorkspaceChange is null)
            {
                WorkspaceDiffText = "当前没有可查看的工作区变更。";
            }

            OnPropertyChanged(nameof(HasWorkspaceChanges));
        }
        catch (Exception ex)
        {
            WorkspaceStatusText = $"读取失败：{ex.Message}";
            WorkspaceDiffText = "无法读取当前项目的 Git 状态。";
        }
        finally
        {
            IsRefreshingWorkspaceChanges = false;
        }
    }

    private async Task LoadSelectedWorkspaceDiffAsync()
    {
        var version = ++_workspaceDiffLoadVersion;
        if (SelectedProject is null || SelectedWorkspaceChange is null)
        {
            WorkspaceDiffText = "选择一个变更文件查看 diff。";
            return;
        }

        WorkspaceDiffText = "正在读取 diff...";
        try
        {
            var diff = await _workspaceChangeService.GetDiffAsync(SelectedProject.Path, SelectedWorkspaceChange.Path);
            if (version != _workspaceDiffLoadVersion)
            {
                return;
            }

            WorkspaceDiffText = diff.HasDiff ? diff.DiffText : "该文件没有未暂存 diff，可能只有暂存区变更或未跟踪状态。";
        }
        catch (Exception ex)
        {
            if (version == _workspaceDiffLoadVersion)
            {
                WorkspaceDiffText = $"读取 diff 失败：{ex.Message}";
            }
        }
    }

    private async Task SendAsync()
    {
        // This is the main chat loop:
        // 1. prepare/persist user message
        // 2. create a placeholder assistant message
        // 3. build a provider-neutral ChatRequest
        // 4. stream ChatDelta values back into the placeholder
        // 5. save final messages and call details
        if (SelectedConversation is null && SelectedProject is not null)
        {
            SelectConversation(SelectedProject.CreateConversation());
        }

        if (SelectedConversation is null)
        {
            return;
        }

        NormalizeProviderSettings();
        var effectiveSettings = CreateEffectiveSettings();
        if (effectiveSettings is null)
        {
            StatusText = "请先在设置中添加模型提供商";
            return;
        }

        var text = DraftMessage.Trim();
        DraftMessage = "";
        // Add the user message before building the provider request so the latest
        // turn is included in context.
        var userMessage = new ChatMessage
        {
            ConversationId = SelectedConversation.Id,
            Role = ChatRole.User,
            Content = text,
            CreatedAt = DateTimeOffset.Now
        };
        SelectedConversation.AddMessage(userMessage);
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(CurrentConversationTitle));
        ApplyConversationFilters();

        var assistantMessage = new ChatMessage
        {
            ConversationId = SelectedConversation.Id,
            Role = ChatRole.Assistant,
            Content = "正在连接模型...",
            CreatedAt = DateTimeOffset.Now
        };
        SelectedConversation.AddMessage(assistantMessage);
        OnPropertyChanged(nameof(HasMessages));
        var assistantViewModel = SelectedConversation.Messages.Last();
        assistantViewModel.IsStreaming = true;
        var hasReceivedContent = false;
        var hasShownToolProgress = false;
        var hasUsedTools = false;
        var callDetail = new LlmCallDetail
        {
            // Call details intentionally capture both the user-facing settings and
            // the exact messages sent. This is vital when learning or debugging agents.
            ConversationId = SelectedConversation.Id,
            UserMessageId = userMessage.Id,
            AssistantMessageId = assistantMessage.Id,
            ProviderName = effectiveSettings.ProviderName,
            Model = effectiveSettings.Model,
            CreatedAt = DateTimeOffset.Now,
            Status = "进行中",
            RequestJson = SerializeJson(new
            {
                provider = effectiveSettings.ProviderName,
                protocol = effectiveSettings.ProtocolId,
                baseUrl = effectiveSettings.BaseUrl,
                model = effectiveSettings.Model,
                temperature = effectiveSettings.Temperature,
                modelParameters = effectiveSettings.ModelParameters,
                enabledTools = ToolOptions
                    .Where(tool => tool.IsEnabled)
                    .Select(tool => tool.Id)
                    .ToList(),
                toolPermissionModes = Settings.ToolPermissionModes,
                messages = SelectedConversation.Conversation.Messages
                    .Where(message => message.Id != assistantMessage.Id && !string.IsNullOrWhiteSpace(message.Content))
                    .Select(message => new
                    {
                        id = message.Id,
                        role = message.Role.ToString().ToLowerInvariant(),
                        content = message.Content
                    })
            })
        };
        SelectedConversation.AddCallDetail(callDetail);

        IsSending = true;
        StatusText = "正在连接模型...";
        _sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var rawResponseEvents = new List<string>();
        var toolTraceByCallId = new Dictionary<string, ToolTraceViewModel>(StringComparer.Ordinal);
        var stepByToolCallId = new Dictionary<string, AgentStepViewModel>(StringComparer.Ordinal);
        var contextMessages = _contextBuilder.Build(new ConversationContextBuildRequest
        {
            Messages = SelectedConversation.Conversation.Messages
                .Where(message => message.Id != assistantMessage.Id && !string.IsNullOrWhiteSpace(message.Content))
                .ToList(),
            Settings = effectiveSettings,
            PromptContext = new SystemPromptContext
            {
                ProjectName = SelectedProject?.Name ?? "AIChat",
                ProjectPath = SelectedProject?.Path ?? Environment.CurrentDirectory,
                EnabledToolIds = Settings.EnabledToolIds,
                ToolPermissionModes = Settings.ToolPermissionModes
            }
        });

        try
        {
            // Give WPF one dispatcher turn so the placeholder message can render
            // before the network call begins.
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { });

            var request = new ChatRequest
            {
                Model = effectiveSettings.Model,
                Temperature = effectiveSettings.Temperature,
                Messages = contextMessages
            };

            await Task.Run(async () =>
            {
                if (_agentHarness is null)
                {
                    await foreach (var delta in _chatService.SendAsync(request, effectiveSettings, _sendCts.Token))
                    {
                        // Preserve raw protocol events separately from rendered content.
                        if (!string.IsNullOrWhiteSpace(delta.RawJson))
                        {
                            rawResponseEvents.Add(delta.RawJson);
                        }

                        if (!string.IsNullOrEmpty(delta.Content))
                        {
                            if (!hasReceivedContent)
                            {
                                // Replace the placeholder text as soon as the first
                                // real token arrives.
                                hasReceivedContent = true;
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    assistantViewModel.Content = "";
                                    StatusText = "模型正在回复...";
                                });
                            }

                            await AppendAssistantContentAsync(assistantViewModel, delta.Content, _sendCts.Token);
                        }
                    }

                    return;
                }

                await foreach (var agentEvent in _agentHarness.RunAsync(
                                   new AgentHarnessRunRequest
                                   {
                                       Conversation = SelectedConversation.Conversation,
                                       UserMessageId = userMessage.Id,
                                       AssistantMessageId = assistantMessage.Id,
                                       Goal = text,
                                       ChatRequest = request,
                                       Settings = effectiveSettings,
                                       Context = new AgentRunContext
                                       {
                                           ProjectPath = SelectedProject?.Path ?? Environment.CurrentDirectory,
                                           EnabledToolIds = Settings.EnabledToolIds,
                                           ToolPermissionModes = Settings.ToolPermissionModes,
                                           RequestToolApprovalAsync = RequestToolApprovalAsync
                                       }
                                   },
                                   _sendCts.Token))
                {
                    switch (agentEvent.Type)
                    {
                        case AgentHarnessEventType.RunStarted:
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (agentEvent.Run is not null)
                                {
                                    assistantViewModel.AttachAgentRun(agentEvent.Run);
                                }
                            });
                            break;
                        case AgentHarnessEventType.StepAdded:
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (agentEvent.Step is not null)
                                {
                                    _ = assistantViewModel.AddAgentStep(agentEvent.Step);
                                }
                            });
                            break;
                        case AgentHarnessEventType.RawProviderEvent:
                            if (!string.IsNullOrWhiteSpace(agentEvent.RawJson))
                            {
                                rawResponseEvents.Add(agentEvent.RawJson);
                            }
                            break;
                        case AgentHarnessEventType.ContentDelta:
                            if (!hasReceivedContent)
                            {
                                hasReceivedContent = true;
                                hasShownToolProgress = false;
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    assistantViewModel.Content = "";
                                    StatusText = "模型正在回复...";
                                });
                            }

                            await AppendAssistantContentAsync(assistantViewModel, agentEvent.Content, _sendCts.Token);
                            break;
                        case AgentHarnessEventType.ToolCall:
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                StatusText = $"调用工具：{agentEvent.ToolCall?.Name}";
                                hasUsedTools = true;
                                if (!hasReceivedContent && !hasShownToolProgress)
                                {
                                    hasShownToolProgress = true;
                                    assistantViewModel.Content = "正在查看项目文件并分析结果...";
                                }

                                if (agentEvent.ToolCall is not null)
                                {
                                    toolTraceByCallId[agentEvent.ToolCall.Id] = assistantViewModel.AddToolTrace(agentEvent.ToolCall);
                                    var stepViewModel = agentEvent.Step is null
                                        ? null
                                        : assistantViewModel.AddAgentStep(agentEvent.Step);
                                    if (stepViewModel is not null)
                                    {
                                        stepByToolCallId[agentEvent.ToolCall.Id] = stepViewModel;
                                    }
                                }
                            });
                            rawResponseEvents.Add(SerializeJson(new
                            {
                                type = "tool_call",
                                id = agentEvent.ToolCall?.Id,
                                name = agentEvent.ToolCall?.Name,
                                arguments = agentEvent.ToolCall?.ArgumentsJson
                            }));
                            break;
                        case AgentHarnessEventType.ToolApprovalRequired:
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                StatusText = $"等待确认工具：{agentEvent.ToolCall?.Name}";
                            });
                            break;
                        case AgentHarnessEventType.ToolApprovalRejected:
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                StatusText = $"已拒绝工具：{agentEvent.ToolCall?.Name}";
                            });
                            break;
                        case AgentHarnessEventType.ToolResult:
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (agentEvent.ToolCall is not null &&
                                    agentEvent.ToolResult is not null &&
                                    toolTraceByCallId.TryGetValue(agentEvent.ToolCall.Id, out var trace))
                                {
                                    trace.Complete(agentEvent.ToolResult.Content, agentEvent.ToolResult.IsError);
                                }

                                if (agentEvent.ToolCall is not null &&
                                    agentEvent.ToolResult is not null &&
                                    stepByToolCallId.TryGetValue(agentEvent.ToolCall.Id, out var step))
                                {
                                    step.Refresh();
                                }

                                assistantViewModel.SyncAgentFileChanges();
                            });
                            rawResponseEvents.Add(SerializeJson(new
                            {
                                type = "tool_result",
                                tool = agentEvent.ToolResult?.ToolName,
                                isError = agentEvent.ToolResult?.IsError,
                                content = agentEvent.ToolResult?.Content
                            }));
                            break;
                        case AgentHarnessEventType.RunCompleted:
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (agentEvent.Step is not null)
                                {
                                    _ = assistantViewModel.AddAgentStep(agentEvent.Step);
                                }

                                if (agentEvent.Run is not null)
                                {
                                    assistantViewModel.SyncAgentFileChanges();
                                    assistantViewModel.AgentRun?.Complete(agentEvent.Run.Status);
                                }
                            });
                            break;
                    }
                }
            }, _sendCts.Token);

            if (!hasReceivedContent)
            {
                assistantViewModel.Content = hasUsedTools
                    ? "已完成工具调用，但模型没有继续返回最终回复。请重试，或打开调用详情查看模型和工具的原始结果。"
                    : "模型没有返回可显示内容。";
            }
            else if (IsProviderErrorContent(assistantViewModel.Content))
            {
                assistantViewModel.IsError = true;
                StatusText = "请求失败";
            }

            if (!assistantViewModel.IsError)
            {
                StatusText = "回复完成";
            }
            await RefreshWorkspaceChangesAsync();
            await CompleteCallDetailAsync(callDetail, "完成", new
            {
                status = "completed",
                provider = effectiveSettings.ProviderName,
                model = effectiveSettings.Model,
                assistantMessageId = assistantMessage.Id,
                content = assistantViewModel.Content,
                rawEvents = NormalizeRawJsonEvents(rawResponseEvents),
                completedAt = DateTimeOffset.Now
            });
        }
        catch (OperationCanceledException)
        {
            if (!hasReceivedContent)
            {
                assistantViewModel.Content = "请求已停止，或模型长时间没有返回内容。";
                assistantViewModel.IsError = true;
            }

            StatusText = "已停止生成";
            assistantViewModel.AgentRun?.Complete(AgentRunStatus.Cancelled);
            await CompleteCallDetailAsync(callDetail, "已停止", new
            {
                status = "cancelled",
                provider = effectiveSettings.ProviderName,
                model = effectiveSettings.Model,
                assistantMessageId = assistantMessage.Id,
                content = assistantViewModel.Content,
                rawEvents = NormalizeRawJsonEvents(rawResponseEvents),
                completedAt = DateTimeOffset.Now
            });
        }
        catch (Exception ex)
        {
            assistantViewModel.Content += $"\n\n请求出错：{ex.Message}";
            assistantViewModel.IsError = true;
            StatusText = "请求失败";
            assistantViewModel.AgentRun?.Complete(AgentRunStatus.Failed);
            await CompleteCallDetailAsync(callDetail, "失败", new
            {
                status = "failed",
                provider = effectiveSettings.ProviderName,
                model = effectiveSettings.Model,
                assistantMessageId = assistantMessage.Id,
                content = assistantViewModel.Content,
                rawEvents = NormalizeRawJsonEvents(rawResponseEvents),
                error = ex.Message,
                exceptionType = ex.GetType().FullName,
                completedAt = DateTimeOffset.Now
            });
        }
        finally
        {
            // Always leave the app in a stable state: stop animation, release the
            // cancellation token, persist messages, and refresh context usage.
            assistantViewModel.IsStreaming = false;
            IsSending = false;
            _sendCts.Dispose();
            _sendCts = null;
            await SaveProjectsAsync();
            UpdateContextUsage();
        }
    }

    private async Task CompleteCallDetailAsync(LlmCallDetail detail, string status, object response)
    {
        // JSON formatting can be a little expensive for large raw event lists, so
        // run it off the UI thread.
        var responseJson = await Task.Run(() => SerializeJson(response));
        detail.Status = status;
        detail.CompletedAt = DateTimeOffset.Now;
        detail.ResponseJson = responseJson;
        SelectedConversation?.RefreshCallDetail(detail);
        if (SelectedCallDetail?.Detail.Id == detail.Id)
        {
            SelectedCallDetail = new LlmCallDetailViewModel(detail);
        }
    }

    private async Task LoadSelectedCallDetailJsonAsync(LlmCallDetailViewModel? detail)
    {
        var version = ++_callDetailLoadVersion;
        if (detail is null)
        {
            SelectedCallRequestJson = CurrentCallDetails?.Count == 0 ? "暂无调用记录。" : "请选择左侧调用记录。";
            SelectedCallResponseJson = CurrentCallDetails?.Count == 0 ? "暂无调用记录。" : "请选择左侧调用记录。";
            return;
        }

        SelectedCallRequestJson = "正在加载入参 JSON...";
        SelectedCallResponseJson = "正在加载出参 JSON...";
        var result = await Task.Run(() => new
        {
            RequestJson = detail.RequestJson,
            ResponseJson = ShowSelectedCallRawEvents ? detail.ResponseJsonWithRawEvents : detail.ResponseJson
        });

        if (version != _callDetailLoadVersion)
        {
            // User selected another call while this one was formatting.
            return;
        }

        SelectedCallRequestJson = result.RequestJson;
        SelectedCallResponseJson = result.ResponseJson;
    }

    private static string SerializeJson(object value)
    {
        return JsonSerializer.Serialize(value, DetailJsonOptions);
    }

    private static bool IsProviderErrorContent(string content)
    {
        return content.StartsWith("LLM 请求失败：", StringComparison.Ordinal) ||
               content.StartsWith("Anthropic 请求失败：", StringComparison.Ordinal) ||
               content.StartsWith("还没有配置", StringComparison.Ordinal);
    }

    private async Task<ToolApprovalDecision> RequestToolApprovalAsync(
        ToolApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var pending = new PendingToolApprovalViewModel(request);
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => PendingToolApproval = pending);
        try
        {
            await using var registration = cancellationToken.Register(() => pending.Reject());
            return await pending.Completion;
        }
        finally
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(PendingToolApproval, pending))
                {
                    PendingToolApproval = null;
                }
            });
        }
    }

    private void ResolvePendingToolApproval(bool allow, bool allowForSession)
    {
        var pending = PendingToolApproval;
        if (pending is null)
        {
            return;
        }

        if (allow)
        {
            pending.Approve(allowForSession);
        }
        else
        {
            pending.Reject();
        }
    }

    private static IReadOnlyList<object> NormalizeRawJsonEvents(IEnumerable<string> rawEvents)
    {
        // Store parsed JSON when possible so the inspector can pretty-print it.
        var normalized = new List<object>();
        foreach (var rawEvent in rawEvents)
        {
            if (string.IsNullOrWhiteSpace(rawEvent))
            {
                continue;
            }

            if (rawEvent == "[DONE]")
            {
                normalized.Add(rawEvent);
                continue;
            }

            try
            {
                normalized.Add(JsonSerializer.Deserialize<JsonElement>(rawEvent));
            }
            catch (JsonException)
            {
                normalized.Add(rawEvent);
            }
        }

        return normalized;
    }

    private Task SaveProjectsAsync()
    {
        return _repository.SaveProjectsAsync(Projects.Select(project => project.Project).ToList());
    }

    private async Task PersistSettingsQuietlyAsync()
    {
        try
        {
            NormalizeProviderSettings();
            await _repository.SaveSettingsAsync(Settings);
        }
        catch
        {
            // Settings persistence should not interrupt typing or model selection.
        }
    }

    private void UpdateContextUsage()
    {
        // Context usage is recomputed whenever selected conversation or model
        // settings change.
        ApplySelectedConfiguredProvider();
        ContextUsage = _contextEstimator.Estimate(
            SelectedConversation?.Conversation.Messages ?? [],
            Settings);
    }

    private async Task AppendAssistantContentAsync(ChatMessageViewModel assistantViewModel, string content, CancellationToken cancellationToken)
    {
        const int chunkSize = 24;
        if (content.Length <= chunkSize)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                assistantViewModel.Content += content;
                UpdateContextUsage();
                ApplyConversationFiltersIfSearching();
            });
            return;
        }

        for (var index = 0; index < content.Length; index += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(chunkSize, content.Length - index);
            var chunk = content.Substring(index, length);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // All bound UI changes must happen on the WPF dispatcher thread.
                assistantViewModel.Content += chunk;
                UpdateContextUsage();
                ApplyConversationFiltersIfSearching();
            });
            await Task.Delay(12, cancellationToken);
        }
    }

    private void NormalizeProviderSettings()
    {
        // Normalize current settings against the catalog and migrate older flat
        // settings into the configured-provider list.
        var provider = ChatProviderCatalog.Resolve(Settings.ProviderId);
        Settings.ProviderId = provider.Id;
        Settings.ProviderName = provider.Name;
        Settings.ProtocolId = provider.ProtocolId;
        Settings.BaseUrl = provider.DefaultBaseUrl;
        Settings.Temperature = AgentDefaultTemperature;

        if (string.IsNullOrWhiteSpace(Settings.Model))
        {
            Settings.Model = provider.DefaultModel;
        }

        var model = ChatProviderCatalog.ResolveModel(provider.Id, Settings.Model);
        Settings.Model = model.Id;
        if (Settings.ModelContextLimit <= 0 || Settings.ModelContextLimit == provider.DefaultContextLimit)
        {
            Settings.ModelContextLimit = model.ContextLimit;
        }

        foreach (var configured in Settings.ConfiguredProviders)
        {
            // Normalize each saved provider so stale IDs/base URLs do not leak
            // into future requests.
            var configuredTemplate = ChatProviderCatalog.Resolve(configured.TemplateId);
            var configuredModel = ChatProviderCatalog.ResolveModel(configuredTemplate.Id, configured.SelectedModelId);
            if (string.IsNullOrWhiteSpace(configured.Id))
            {
                configured.Id = Guid.NewGuid().ToString("N");
            }

            configured.TemplateId = configuredTemplate.Id;
            configured.ProtocolId = configuredTemplate.ProtocolId;
            configured.Name = configuredTemplate.Name;
            configured.BaseUrl = configuredTemplate.DefaultBaseUrl;
            configured.SelectedModelId = configuredModel.Id;
            configured.ModelParameters = NormalizeModelParameterValues(configuredTemplate.Id, configuredModel.Id, configured.ModelParameters);
        }

        DeduplicateConfiguredProviders();
        OnPropertyChanged(nameof(SelectedProviderId));
        OnPropertyChanged(nameof(SelectedActiveModelId));
        OnPropertyChanged(nameof(ActiveModelOptions));
        if (Settings.ConfiguredProviders.Count == 0 && !string.IsNullOrWhiteSpace(Settings.ApiKey))
        {
            Settings.ConfiguredProviders.Add(new ConfiguredLlmProvider
            {
                TemplateId = provider.Id,
                ProtocolId = provider.ProtocolId,
                Name = provider.Name,
                BaseUrl = Settings.BaseUrl,
                ApiKey = Settings.ApiKey,
                SelectedModelId = Settings.Model,
                ModelParameters = NormalizeModelParameterValues(provider.Id, Settings.Model, Settings.ModelParameters)
            });
        }

        if (string.IsNullOrWhiteSpace(Settings.ActiveConfiguredProviderId) && Settings.ConfiguredProviders.Count > 0)
        {
            Settings.ActiveConfiguredProviderId = Settings.ConfiguredProviders[0].Id;
        }
        else if (Settings.ConfiguredProviders.Count > 0 &&
                 Settings.ConfiguredProviders.All(provider => provider.Id != Settings.ActiveConfiguredProviderId))
        {
            Settings.ActiveConfiguredProviderId = Settings.ConfiguredProviders[0].Id;
        }

        ApplySelectedConfiguredProvider();
        OnPropertyChanged(nameof(ConfiguredProviders));
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(SelectedConfiguredProviderId));
        OnPropertyChanged(nameof(SelectedConfiguredProvider));
        OnPropertyChanged(nameof(SelectedActiveModelId));
        OnPropertyChanged(nameof(ActiveModelOptions));
    }

    private void NormalizeToolSettings()
    {
        if (_toolCatalog is null)
        {
            return;
        }

        var knownIds = _toolCatalog.All.Select(tool => tool.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Settings.EnabledToolIds = Settings.EnabledToolIds
            .Where(knownIds.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (Settings.EnabledToolIds.Count == 0)
        {
            Settings.EnabledToolIds = _toolCatalog.All
                .Select(tool => tool.Id)
                .ToList();
        }
        else if (Settings.EnabledToolIds.Contains("git_status", StringComparer.OrdinalIgnoreCase) &&
                 Settings.EnabledToolIds.Contains("git_diff", StringComparer.OrdinalIgnoreCase) &&
                 knownIds.Contains("git_restore_file"))
        {
            AddEnabledToolIfKnown("git_restore_file");
            AddEnabledToolIfKnown("git_commit");
        }

        Settings.ToolPermissionModes = Settings.ToolPermissionModes
            .Where(entry => knownIds.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var tool in _toolCatalog.All)
        {
            Settings.ToolPermissionModes.TryAdd(tool.Id, DefaultPermissionMode(tool));
        }

        void AddEnabledToolIfKnown(string toolId)
        {
            if (knownIds.Contains(toolId) &&
                !Settings.EnabledToolIds.Contains(toolId, StringComparer.OrdinalIgnoreCase))
            {
                Settings.EnabledToolIds.Add(toolId);
            }
        }
    }

    private void NormalizeModelParameters()
    {
        var configured = SelectedConfiguredProvider;
        if (configured is null)
        {
            Settings.ModelParameters = [];
            return;
        }

        configured.ModelParameters = NormalizeModelParameterValues(
            configured.TemplateId,
            configured.SelectedModelId,
            configured.ModelParameters);
        Settings.ModelParameters = new Dictionary<string, string>(configured.ModelParameters, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> NormalizeModelParameterValues(
        string providerId,
        string modelId,
        IDictionary<string, string>? values)
    {
        var model = ChatProviderCatalog.ResolveModel(providerId, modelId);
        var known = model.Parameters.ToDictionary(parameter => parameter.Id, StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values is not null)
        {
            foreach (var entry in values)
            {
                if (!known.TryGetValue(entry.Key, out var parameter))
                {
                    continue;
                }

                var value = entry.Value ?? "";
                if (parameter.Options.Count > 0 &&
                    parameter.Options.All(option => !string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)))
                {
                    value = parameter.DefaultValue;
                }

                normalized[parameter.Id] = value;
            }
        }

        foreach (var parameter in model.Parameters)
        {
            normalized.TryAdd(parameter.Id, parameter.DefaultValue);
        }

        return normalized;
    }

    private void RebuildToolOptions()
    {
        if (_toolCatalog is null)
        {
            return;
        }

        var enabled = Settings.EnabledToolIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ToolOptions.Clear();
        foreach (var tool in _toolCatalog.All)
        {
            var mode = Settings.ToolPermissionModes.TryGetValue(tool.Id, out var configuredMode)
                ? configuredMode
                : DefaultPermissionMode(tool);
            ToolOptions.Add(new ToolOptionViewModel
            {
                Id = tool.Id,
                Name = tool.Definition.Name,
                Description = tool.Definition.Description,
                RiskLabel = tool.Risk switch
                {
                    AgentToolRisk.ReadOnly => "只读",
                    AgentToolRisk.Write => "写入",
                    AgentToolRisk.Shell => "Shell",
                    _ => "工具"
                },
                PermissionModeOptions = ToolPermissionModeOptions,
                IsEnabled = enabled.Contains(tool.Id),
                PermissionMode = mode.ToString()
            });
        }

        OnPropertyChanged(nameof(ToolOptions));
    }

    private void SyncToolOptionsToSettings()
    {
        Settings.EnabledToolIds = ToolOptions
            .Where(tool => tool.IsEnabled && !string.Equals(tool.PermissionMode, nameof(ToolPermissionMode.Disabled), StringComparison.OrdinalIgnoreCase))
            .Select(tool => tool.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Settings.ToolPermissionModes = ToolOptions.ToDictionary(
            tool => tool.Id,
            tool => Enum.TryParse<ToolPermissionMode>(tool.PermissionMode, out var mode) ? mode : ToolPermissionMode.ConfirmEachTime,
            StringComparer.OrdinalIgnoreCase);
    }

    private static ToolPermissionMode DefaultPermissionMode(IAgentTool tool)
    {
        return tool.Risk == AgentToolRisk.ReadOnly
            ? ToolPermissionMode.AutoReadOnly
            : ToolPermissionMode.ConfirmEachTime;
    }

    private void RebuildModelParameterOptions()
    {
        var configured = SelectedConfiguredProvider;
        ModelParameterOptions.Clear();
        if (configured is null)
        {
            RaiseModelParameterOptionChanges();
            return;
        }

        var model = ChatProviderCatalog.ResolveModel(configured.TemplateId, configured.SelectedModelId);
        var values = NormalizeModelParameterValues(configured.TemplateId, model.Id, configured.ModelParameters);
        configured.ModelParameters = values;
        Settings.ModelParameters = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in model.Parameters)
        {
            var option = new ModelParameterOptionViewModel
            {
                Id = parameter.Id,
                Name = parameter.DisplayName,
                Description = parameter.Description,
                SelectedValue = values.TryGetValue(parameter.Id, out var value) ? value : parameter.DefaultValue
            };
            foreach (var parameterOption in parameter.Options)
            {
                option.Options.Add(parameterOption);
            }

            ModelParameterOptions.Add(option);
        }

        RaiseModelParameterOptionChanges();
    }

    private void SyncModelParameterOptionsToSettings()
    {
        var values = ModelParameterOptions.ToDictionary(
            parameter => parameter.Id,
            parameter => parameter.SelectedValue,
            StringComparer.OrdinalIgnoreCase);
        Settings.ModelParameters = values;
        var configured = SelectedConfiguredProvider;
        if (configured is not null)
        {
            configured.ModelParameters = NormalizeModelParameterValues(
                configured.TemplateId,
                configured.SelectedModelId,
                values);
        }
    }

    private void RaiseModelParameterOptionChanges()
    {
        OnPropertyChanged(nameof(ModelParameterOptions));
        OnPropertyChanged(nameof(HasModelParameterOptions));
        OnPropertyChanged(nameof(ActiveModelCapabilitySummary));
    }

    private async Task AddConfiguredProviderAsync()
    {
        var template = ChatProviderCatalog.Resolve(SelectedProviderId);
        var model = ChatProviderCatalog.ResolveModel(template.Id, template.DefaultModel);
        var apiKey = NewProviderApiKey.Trim();
        var existing = Settings.ConfiguredProviders.FirstOrDefault(provider =>
            string.Equals(provider.TemplateId, template.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(provider.ApiKey, apiKey, StringComparison.Ordinal));
        if (existing is not null)
        {
            // Avoid duplicate entries for the same template/API key pair.
            Settings.ActiveConfiguredProviderId = existing.Id;
            existing.ProtocolId = template.ProtocolId;
            existing.Name = template.Name;
            existing.BaseUrl = template.DefaultBaseUrl;
            existing.SelectedModelId = ChatProviderCatalog.ResolveModel(template.Id, existing.SelectedModelId).Id;
            existing.ModelParameters = NormalizeModelParameterValues(template.Id, existing.SelectedModelId, existing.ModelParameters);
            NewProviderApiKey = "";
            ApplySelectedConfiguredProvider();
            await _repository.SaveSettingsAsync(Settings);
            RaiseConfiguredProviderChanges();
            StatusText = "该模型提供商已存在，已切换到该配置";
            return;
        }

        var configured = new ConfiguredLlmProvider
        {
            TemplateId = template.Id,
            ProtocolId = template.ProtocolId,
            Name = template.Name,
            BaseUrl = template.DefaultBaseUrl,
            ApiKey = apiKey,
            SelectedModelId = model.Id,
            ModelParameters = NormalizeModelParameterValues(template.Id, model.Id, null)
        };
        Settings.ConfiguredProviders.Add(configured);
        Settings.ActiveConfiguredProviderId = configured.Id;
        NewProviderApiKey = "";
        ApplySelectedConfiguredProvider();
        await _repository.SaveSettingsAsync(Settings);
        RaiseConfiguredProviderChanges();
        StatusText = $"{configured.Name} 已添加";
    }

    private async Task TestProviderConnectionAsync()
    {
        var apiKey = NewProviderApiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText = "请先输入 API Key";
            return;
        }

        var template = ChatProviderCatalog.Resolve(SelectedProviderId);
        IsTestingProviderConnection = true;
        StatusText = "正在测试模型连接...";
        try
        {
            // A lightweight /models request proves the API key and base URL are
            // at least reachable before saving a provider entry.
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{template.DefaultBaseUrl.TrimEnd('/')}/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await httpClient.SendAsync(request);
            StatusText = response.IsSuccessStatusCode
                ? "连接测试通过"
                : $"连接测试失败：{(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (Exception ex)
        {
            StatusText = $"连接测试失败：{ex.Message}";
        }
        finally
        {
            IsTestingProviderConnection = false;
        }
    }

    private async Task RemoveConfiguredProviderAsync()
    {
        var configured = SelectedConfiguredProvider;
        if (configured is null)
        {
            return;
        }

        Settings.ConfiguredProviders.Remove(configured);
        Settings.ActiveConfiguredProviderId = Settings.ConfiguredProviders.FirstOrDefault()?.Id ?? "";
        ApplySelectedConfiguredProvider();
        await _repository.SaveSettingsAsync(Settings);
        RaiseConfiguredProviderChanges();
        StatusText = "模型提供商已移除";
    }

    private AppSettings? CreateEffectiveSettings()
    {
        var configured = SelectedConfiguredProvider;
        if (configured is null || string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            return null;
        }

        var template = ChatProviderCatalog.Resolve(configured.TemplateId);
        var model = ChatProviderCatalog.ResolveModel(template.Id, configured.SelectedModelId);
        // Build a request-ready settings object from the selected saved provider.
        configured.TemplateId = template.Id;
        configured.ProtocolId = template.ProtocolId;
        configured.Name = template.Name;
        configured.BaseUrl = template.DefaultBaseUrl;
        configured.SelectedModelId = model.Id;
        return new AppSettings
        {
            ProviderId = configured.TemplateId,
            ProtocolId = configured.ProtocolId,
            ProviderName = configured.Name,
            BaseUrl = configured.BaseUrl,
            ApiKey = configured.ApiKey,
            Model = model.Id,
            Temperature = AgentDefaultTemperature,
            ModelContextLimit = model.ContextLimit,
            ModelParameters = NormalizeModelParameterValues(configured.TemplateId, model.Id, configured.ModelParameters),
            ActiveConfiguredProviderId = configured.Id,
            ConfiguredProviders = Settings.ConfiguredProviders
        };
    }

    private void ApplySelectedConfiguredProvider()
    {
        var configured = SelectedConfiguredProvider;
        if (configured is null)
        {
            return;
        }

        var template = ChatProviderCatalog.Resolve(configured.TemplateId);
        var model = ChatProviderCatalog.ResolveModel(template.Id, configured.SelectedModelId);
        // Keep legacy flat settings in sync with the active configured provider.
        Settings.ProviderId = configured.TemplateId;
        Settings.ProtocolId = configured.ProtocolId;
        Settings.ProviderName = configured.Name;
        Settings.BaseUrl = configured.BaseUrl;
        Settings.ApiKey = configured.ApiKey;
        Settings.Model = model.Id;
        Settings.ModelContextLimit = model.ContextLimit;
        Settings.ModelParameters = NormalizeModelParameterValues(configured.TemplateId, model.Id, configured.ModelParameters);
    }

    private void RaiseConfiguredProviderChanges()
    {
        OnPropertyChanged(nameof(ConfiguredProviders));
        OnPropertyChanged(nameof(SelectedConfiguredProvider));
        OnPropertyChanged(nameof(SelectedConfiguredProviderId));
        OnPropertyChanged(nameof(ActiveModelOptions));
        OnPropertyChanged(nameof(SelectedActiveModelId));
        RebuildModelParameterOptions();
        OnPropertyChanged(nameof(ModelName));
        OnPropertyChanged(nameof(HasApiKey));
        RemoveConfiguredProviderCommand.RaiseCanExecuteChanged();
    }

    private void ApplyConversationFilters()
    {
        foreach (var project in Projects)
        {
            project.ApplyConversationFilter(ConversationSearchText);
        }
    }

    private void ApplyConversationFiltersIfSearching()
    {
        if (!string.IsNullOrWhiteSpace(ConversationSearchText))
        {
            ApplyConversationFilters();
        }
    }

    private void DeduplicateConfiguredProviders()
    {
        if (Settings.ConfiguredProviders.Count < 2)
        {
            return;
        }

        var activeId = Settings.ActiveConfiguredProviderId;
        // Prefer keeping the active duplicate so the current UI selection is stable.
        var uniqueProviders = Settings.ConfiguredProviders
            .GroupBy(provider => $"{provider.TemplateId}|{provider.ApiKey}", StringComparer.Ordinal)
            .Select(group =>
                group.FirstOrDefault(provider => provider.Id == activeId) ??
                group.First())
            .ToList();

        if (uniqueProviders.Count == Settings.ConfiguredProviders.Count)
        {
            return;
        }

        Settings.ConfiguredProviders.Clear();
        Settings.ConfiguredProviders.AddRange(uniqueProviders);
        if (Settings.ConfiguredProviders.All(provider => provider.Id != activeId))
        {
            Settings.ActiveConfiguredProviderId = Settings.ConfiguredProviders.FirstOrDefault()?.Id ?? "";
        }
    }
}
