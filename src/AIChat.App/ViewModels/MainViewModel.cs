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
    private bool _isStopping;
    private string _statusText = "就绪";
    private ContextUsage _contextUsage = new() { ModelLimit = 128_000, ConversationLimit = 64_000 };
    private CancellationTokenSource? _sendCts;
    private bool _isSettingsOpen;
    private bool _isCallDetailsOpen;
    private bool _isAgentRunHistoryOpen;
    private bool _isAgentRunDetailsOpen;
    private bool _isNewProviderApiKeyVisible;
    private bool _isTestingProviderConnection;
    private PendingToolApprovalViewModel? _pendingToolApproval;
    private string _newProviderApiKey = "";
    private string _conversationSearchText = "";
    private ConversationViewModel? _callDetailsConversation;
    private LlmCallDetailViewModel? _selectedCallDetail;
    private AgentRunHistoryItemViewModel? _selectedAgentRunHistoryItem;
    private AgentRunViewModel? _selectedAgentRunDetails;
    private int _agentRunHistoryTotalCount;
    private string _selectedCallRequestJson = "请选择左侧调用记录。";
    private string _selectedCallResponseJson = "请选择左侧调用记录。";
    private bool _showSelectedCallRawEvents;
    private WorkspaceChangeViewModel? _selectedWorkspaceChange;
    private string _agentRunHistoryFilterId = "all";
    private string _workspaceBranch = "";
    private string _workspaceStatusText = "尚未刷新";
    private string _workspaceDiffText = "选择一个变更文件查看 diff。";
    private IReadOnlyList<DiffLineViewModel> _workspaceDiffLines = [];
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
        StopCommand = new RelayCommand(_ => StopCurrentRun(), _ => IsSending && !IsStopping);
        CopyMessageCommand = new RelayCommand(parameter => CopyMessage((ChatMessageViewModel)parameter!));
        CopyConversationTitleCommand = new RelayCommand(parameter => CopyConversationTitle((ConversationViewModel)parameter!));
        RenameConversationCommand = new RelayCommand(async parameter => await RenameConversationAsync((ConversationViewModel)parameter!), parameter => parameter is ConversationViewModel);
        DeleteConversationCommand = new RelayCommand(async parameter => await DeleteConversationAsync((ConversationViewModel)parameter!), parameter => parameter is ConversationViewModel);
        OpenCallDetailsCommand = new RelayCommand(parameter => OpenCallDetails((ConversationViewModel)parameter!), parameter => parameter is ConversationViewModel);
        CloseCallDetailsCommand = new RelayCommand(_ => IsCallDetailsOpen = false);
        OpenAgentRunHistoryCommand = new RelayCommand(_ => OpenAgentRunHistory(), _ => SelectedProject is not null);
        CloseAgentRunHistoryCommand = new RelayCommand(_ => IsAgentRunHistoryOpen = false);
        SelectAgentRunHistoryItemCommand = new RelayCommand(parameter => SelectAgentRunHistoryItem((AgentRunHistoryItemViewModel)parameter!), parameter => parameter is AgentRunHistoryItemViewModel);
        RetryAgentRunCommand = new RelayCommand(parameter => RetryAgentRun((AgentRunHistoryItemViewModel)parameter!), parameter => parameter is AgentRunHistoryItemViewModel { CanRetry: true } && !IsSending);
        OpenAgentRunDetailsCommand = new RelayCommand(parameter => OpenAgentRunDetails((ChatMessageViewModel)parameter!), parameter => parameter is ChatMessageViewModel { AgentRun: not null });
        CloseAgentRunDetailsCommand = new RelayCommand(_ => IsAgentRunDetailsOpen = false);
        AddConfiguredProviderCommand = new RelayCommand(async _ => await AddConfiguredProviderAsync(), _ => !string.IsNullOrWhiteSpace(NewProviderApiKey));
        RemoveConfiguredProviderCommand = new RelayCommand(async _ => await RemoveConfiguredProviderAsync(), _ => SelectedConfiguredProvider is not null);
        ToggleNewProviderApiKeyVisibilityCommand = new RelayCommand(_ => IsNewProviderApiKeyVisible = !IsNewProviderApiKeyVisible);
        TestProviderConnectionCommand = new RelayCommand(async _ => await TestProviderConnectionAsync(), _ => !IsTestingProviderConnection && !string.IsNullOrWhiteSpace(NewProviderApiKey));
        RefreshWorkspaceChangesCommand = new RelayCommand(async _ => await RefreshWorkspaceChangesAsync(), _ => SelectedProject is not null && !IsRefreshingWorkspaceChanges);
        RestoreWorkspaceFileCommand = new RelayCommand(async _ => await RestoreSelectedWorkspaceChangesAsync(), _ => (SelectedWorkspaceChange is not null || HasSelectedWorkspaceChanges) && !IsRefreshingWorkspaceChanges);
        CommitWorkspaceFileCommand = new RelayCommand(async _ => await CommitSelectedWorkspaceFileAsync(), _ => SelectedWorkspaceChange is not null && !IsRefreshingWorkspaceChanges);
        CommitAllWorkspaceChangesCommand = new RelayCommand(async _ => await CommitAllWorkspaceChangesAsync(), _ => HasSelectedWorkspaceChanges && !IsRefreshingWorkspaceChanges);
        OpenWorkspaceFileCommand = new RelayCommand(_ => OpenWorkspaceFile(), _ => SelectedWorkspaceChange is not null && SelectedProject is not null);
        CopyWorkspacePathCommand = new RelayCommand(_ => CopyWorkspacePath(), _ => SelectedWorkspaceChange is not null);
        CopyWorkspaceDiffCommand = new RelayCommand(_ => CopyWorkspaceDiff(), _ => !string.IsNullOrWhiteSpace(WorkspaceDiffText));
        StageSelectedWorkspaceChangesCommand = new RelayCommand(async _ => await StageSelectedWorkspaceChangesAsync(), _ => HasSelectedWorkspaceChanges && !IsRefreshingWorkspaceChanges);
        UnstageSelectedWorkspaceChangesCommand = new RelayCommand(async _ => await UnstageSelectedWorkspaceChangesAsync(), _ => HasSelectedWorkspaceChanges && !IsRefreshingWorkspaceChanges);
        SelectAllWorkspaceChangesCommand = new RelayCommand(_ => SetWorkspaceSelection(isSelected: true), _ => HasWorkspaceChanges);
        ClearWorkspaceSelectionCommand = new RelayCommand(_ => SetWorkspaceSelection(isSelected: false), _ => HasSelectedWorkspaceChanges);
        CommitAgentRunChangesCommand = new RelayCommand(async parameter => await CommitAgentRunChangesAsync((ChatMessageViewModel)parameter!), CanOperateAgentRunChanges);
        RestoreAgentRunChangesCommand = new RelayCommand(async parameter => await RestoreAgentRunChangesAsync((ChatMessageViewModel)parameter!), CanOperateAgentRunChanges);
        CopyAgentRunChangeSummaryCommand = new RelayCommand(parameter => CopyAgentRunChangeSummary((ChatMessageViewModel)parameter!), CanOperateAgentRunChanges);
        CopySelectedAgentRunSummaryCommand = new RelayCommand(_ => CopySelectedAgentRunSummary(), _ => SelectedAgentRunDetails is not null);
        CopySelectedAgentRunReviewPacketCommand = new RelayCommand(_ => CopySelectedAgentRunReviewPacket(), _ => SelectedAgentRunDetails is not null);
        RetrySelectedAgentRunCommand = new RelayCommand(_ => RetrySelectedAgentRun(), _ => SelectedAgentRunDetails?.CanRetry == true && !IsSending);
        OpenAgentFileChangeCommand = new RelayCommand(parameter => OpenAgentFileChange((AgentFileChangeViewModel)parameter!), parameter => parameter is AgentFileChangeViewModel);
        CopyAgentFilePathCommand = new RelayCommand(parameter => CopyAgentFilePath((AgentFileChangeViewModel)parameter!), parameter => parameter is AgentFileChangeViewModel);
        CopyAgentFileDiffCommand = new RelayCommand(parameter => CopyAgentFileDiff((AgentFileChangeViewModel)parameter!), parameter => parameter is AgentFileChangeViewModel { HasDiff: true });
        ApproveToolCommand = new RelayCommand(_ => ResolvePendingToolApproval(allow: true, allowForSession: false), _ => PendingToolApproval is not null);
        ApproveToolForSessionCommand = new RelayCommand(_ => ResolvePendingToolApproval(allow: true, allowForSession: true), _ => PendingToolApproval is not null);
        RejectToolCommand = new RelayCommand(_ => ResolvePendingToolApproval(allow: false, allowForSession: false), _ => PendingToolApproval is not null);
    }

    public ObservableCollection<ProjectViewModel> Projects { get; } = [];
    public ObservableCollection<ToolOptionViewModel> ToolOptions { get; } = [];
    public ObservableCollection<ModelParameterOptionViewModel> ModelParameterOptions { get; } = [];
    public ObservableCollection<WorkspaceChangeViewModel> WorkspaceChanges { get; } = [];
    public ObservableCollection<WorkspaceChangeViewModel> StagedWorkspaceChanges { get; } = [];
    public ObservableCollection<WorkspaceChangeViewModel> UnstagedWorkspaceChanges { get; } = [];
    public ObservableCollection<WorkspaceChangeViewModel> UntrackedWorkspaceChanges { get; } = [];
    public ObservableCollection<AgentRunHistoryItemViewModel> AgentRunHistory { get; } = [];
    public IReadOnlyList<SelectionOptionViewModel> AgentRunHistoryFilterOptions { get; } =
    [
        new() { Id = "all", Name = "全部" },
        new() { Id = "retryable", Name = "可重试" },
        new() { Id = "failed", Name = "失败/停止" },
        new() { Id = "completed", Name = "已完成" },
        new() { Id = "running", Name = "运行中" }
    ];
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
    public RelayCommand OpenAgentRunHistoryCommand { get; }
    public RelayCommand CloseAgentRunHistoryCommand { get; }
    public RelayCommand SelectAgentRunHistoryItemCommand { get; }
    public RelayCommand RetryAgentRunCommand { get; }
    public RelayCommand OpenAgentRunDetailsCommand { get; }
    public RelayCommand CloseAgentRunDetailsCommand { get; }
    public RelayCommand AddConfiguredProviderCommand { get; }
    public RelayCommand RemoveConfiguredProviderCommand { get; }
    public RelayCommand ToggleNewProviderApiKeyVisibilityCommand { get; }
    public RelayCommand TestProviderConnectionCommand { get; }
    public RelayCommand RefreshWorkspaceChangesCommand { get; }
    public RelayCommand RestoreWorkspaceFileCommand { get; }
    public RelayCommand CommitWorkspaceFileCommand { get; }
    public RelayCommand CommitAllWorkspaceChangesCommand { get; }
    public RelayCommand OpenWorkspaceFileCommand { get; }
    public RelayCommand CopyWorkspacePathCommand { get; }
    public RelayCommand CopyWorkspaceDiffCommand { get; }
    public RelayCommand StageSelectedWorkspaceChangesCommand { get; }
    public RelayCommand UnstageSelectedWorkspaceChangesCommand { get; }
    public RelayCommand SelectAllWorkspaceChangesCommand { get; }
    public RelayCommand ClearWorkspaceSelectionCommand { get; }
    public RelayCommand CommitAgentRunChangesCommand { get; }
    public RelayCommand RestoreAgentRunChangesCommand { get; }
    public RelayCommand CopyAgentRunChangeSummaryCommand { get; }
    public RelayCommand CopySelectedAgentRunSummaryCommand { get; }
    public RelayCommand CopySelectedAgentRunReviewPacketCommand { get; }
    public RelayCommand RetrySelectedAgentRunCommand { get; }
    public RelayCommand OpenAgentFileChangeCommand { get; }
    public RelayCommand CopyAgentFilePathCommand { get; }
    public RelayCommand CopyAgentFileDiffCommand { get; }
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
                OnPropertyChanged(nameof(AgentMaxToolRounds));
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
                OnPropertyChanged(nameof(AgentRunHistoryTitle));
                NewChatCommand.RaiseCanExecuteChanged();
                OpenAgentRunHistoryCommand.RaiseCanExecuteChanged();
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
                RebuildAgentRunHistory();
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
    public bool HasSelectedWorkspaceChanges => WorkspaceChanges.Any(change => change.IsSelected);
    public string WorkspaceSelectionText
    {
        get
        {
            var selectedCount = WorkspaceChanges.Count(change => change.IsSelected);
            return selectedCount == 0 ? "未选择文件" : $"已选择 {selectedCount} 个文件";
        }
    }
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
        private set
        {
            if (SetProperty(ref _workspaceDiffText, value))
            {
                WorkspaceDiffLines = DiffLineViewModel.FromDiff(value);
                CopyWorkspaceDiffCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public IReadOnlyList<DiffLineViewModel> WorkspaceDiffLines
    {
        get => _workspaceDiffLines;
        private set => SetProperty(ref _workspaceDiffLines, value);
    }
    public bool IsRefreshingWorkspaceChanges
    {
        get => _isRefreshingWorkspaceChanges;
        private set
        {
            if (SetProperty(ref _isRefreshingWorkspaceChanges, value))
            {
                RaiseWorkspaceCommandStates();
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
                RestoreWorkspaceFileCommand.RaiseCanExecuteChanged();
                CommitWorkspaceFileCommand.RaiseCanExecuteChanged();
                OpenWorkspaceFileCommand.RaiseCanExecuteChanged();
                CopyWorkspacePathCommand.RaiseCanExecuteChanged();
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

    public bool IsAgentRunHistoryOpen
    {
        get => _isAgentRunHistoryOpen;
        set => SetProperty(ref _isAgentRunHistoryOpen, value);
    }

    public bool IsAgentRunDetailsOpen
    {
        get => _isAgentRunDetailsOpen;
        set => SetProperty(ref _isAgentRunDetailsOpen, value);
    }

    public AgentRunHistoryItemViewModel? SelectedAgentRunHistoryItem
    {
        get => _selectedAgentRunHistoryItem;
        private set => SetProperty(ref _selectedAgentRunHistoryItem, value);
    }

    public bool HasAgentRunHistory => AgentRunHistory.Count > 0;
    public string AgentRunHistoryFilterId
    {
        get => _agentRunHistoryFilterId;
        set
        {
            if (SetProperty(ref _agentRunHistoryFilterId, string.IsNullOrWhiteSpace(value) ? "all" : value))
            {
                RebuildAgentRunHistory();
            }
        }
    }
    public string AgentRunHistoryTitle => SelectedProject is null
        ? "运行历史"
        : $"{SelectedProject.Name} · 运行历史";
    public string AgentRunHistorySummary => AgentRunHistory.Count == 0
        ? _agentRunHistoryTotalCount == 0
            ? "暂无 Agent 运行记录"
            : $"当前筛选无匹配 · 总计 {_agentRunHistoryTotalCount} 次运行"
        : $"显示 {AgentRunHistory.Count} / {_agentRunHistoryTotalCount} 次运行 · {AgentRunHistory.Count(item => item.CanRetry)} 个可重试";

    public AgentRunViewModel? SelectedAgentRunDetails
    {
        get => _selectedAgentRunDetails;
        private set
        {
            if (SetProperty(ref _selectedAgentRunDetails, value))
            {
                OnPropertyChanged(nameof(AgentRunDetailsTitle));
                CopySelectedAgentRunSummaryCommand.RaiseCanExecuteChanged();
                CopySelectedAgentRunReviewPacketCommand.RaiseCanExecuteChanged();
                RetrySelectedAgentRunCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string AgentRunDetailsTitle => SelectedAgentRunDetails is null
        ? "Agent Run"
        : $"Agent Run · {SelectedAgentRunDetails.StatusText}";

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
                RetryAgentRunCommand.RaiseCanExecuteChanged();
                RetrySelectedAgentRunCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsStopping
    {
        get => _isStopping;
        private set
        {
            if (SetProperty(ref _isStopping, value))
            {
                StopCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(StopButtonText));
            }
        }
    }

    public string StopButtonText => IsStopping ? "停止中" : "停止";

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
    public int AgentMaxToolRounds
    {
        get => Settings.AgentMaxToolRounds;
        set
        {
            var normalized = Math.Clamp(value, 1, 20);
            if (Settings.AgentMaxToolRounds == normalized)
            {
                return;
            }

            Settings.AgentMaxToolRounds = normalized;
            OnPropertyChanged();
        }
    }

    private bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(DraftMessage) && SelectedProject is not null;

    public void ConfigureAgent(AgentHarness agentHarness, AgentToolCatalog toolCatalog)
    {
        _agentHarness = agentHarness;
        _toolCatalog = toolCatalog;
        RebuildToolOptions();
    }

    private void StopCurrentRun()
    {
        if (_sendCts is null || IsStopping)
        {
            return;
        }

        IsStopping = true;
        StatusText = "正在停止生成...";
        _sendCts.Cancel();
    }

    public async Task InitializeAsync()
    {
        // Startup sequence: load settings, normalize old values, load projects,
        // then select the default project/conversation for the UI.
        Settings = await _repository.LoadSettingsAsync();
        NormalizeProviderSettings();
        NormalizeToolSettings();
        NormalizeHarnessSettings();
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
        NormalizeHarnessSettings();
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

    private void RebuildAgentRunHistory()
    {
        AgentRunHistory.Clear();
        if (SelectedProject is null)
        {
            _agentRunHistoryTotalCount = 0;
            RaiseAgentRunHistoryProperties();
            return;
        }

        var allItems = SelectedProject.Conversations
            .SelectMany(conversation => conversation.Messages
                .Where(message => message.AgentRun is not null)
                .Select(message => new AgentRunHistoryItemViewModel
                {
                    Conversation = conversation,
                    Run = message.AgentRun!
                }))
            .OrderByDescending(item => item.Run.Run.StartedAt)
            .ToList();

        _agentRunHistoryTotalCount = allItems.Count;
        var items = FilterAgentRunHistory(allItems).ToList();

        foreach (var item in items)
        {
            AgentRunHistory.Add(item);
        }

        RaiseAgentRunHistoryProperties();
    }

    private void RaiseAgentRunHistoryProperties()
    {
        OnPropertyChanged(nameof(HasAgentRunHistory));
        OnPropertyChanged(nameof(AgentRunHistorySummary));
        RetryAgentRunCommand.RaiseCanExecuteChanged();
    }

    private IEnumerable<AgentRunHistoryItemViewModel> FilterAgentRunHistory(IEnumerable<AgentRunHistoryItemViewModel> items)
    {
        return AgentRunHistoryFilterId switch
        {
            "retryable" => items.Where(item => item.CanRetry),
            "failed" => items.Where(item => item.Run.Status is AgentRunStatus.Failed or AgentRunStatus.Cancelled),
            "completed" => items.Where(item => item.Run.Status is AgentRunStatus.Completed),
            "running" => items.Where(item => item.Run.Status is AgentRunStatus.Running),
            _ => items
        };
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

    private void OpenAgentRunDetails(ChatMessageViewModel message)
    {
        if (message.AgentRun is null)
        {
            return;
        }

        SelectedAgentRunDetails = message.AgentRun;
        IsAgentRunDetailsOpen = true;
    }

    private void OpenAgentRunHistory()
    {
        RebuildAgentRunHistory();
        IsAgentRunHistoryOpen = true;
    }

    private void SelectAgentRunHistoryItem(AgentRunHistoryItemViewModel item)
    {
        SelectedAgentRunHistoryItem = item;
        SelectedAgentRunDetails = item.Run;
        IsAgentRunDetailsOpen = true;
    }

    private void RetryAgentRun(AgentRunHistoryItemViewModel item)
    {
        if (!item.CanRetry || IsSending)
        {
            return;
        }

        SelectConversation(item.Conversation);
        DraftMessage = item.Run.RecoverySuggestion;
        IsAgentRunHistoryOpen = false;
        IsAgentRunDetailsOpen = false;
        StatusText = "已把恢复建议放回输入框，可检查后重新发送";
    }

    private void RetrySelectedAgentRun()
    {
        var selected = SelectedAgentRunDetails;
        if (selected is null || !selected.CanRetry || IsSending)
        {
            return;
        }

        var historyItem = AgentRunHistory.FirstOrDefault(item => item.Run.Id == selected.Id);
        if (historyItem is not null)
        {
            RetryAgentRun(historyItem);
            return;
        }

        DraftMessage = selected.RecoverySuggestion;
        IsAgentRunDetailsOpen = false;
        StatusText = "已把恢复建议放回输入框，可检查后重新发送";
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
            StagedWorkspaceChanges.Clear();
            UnstagedWorkspaceChanges.Clear();
            UntrackedWorkspaceChanges.Clear();
            foreach (var change in changeSet.Changes)
            {
                var viewModel = new WorkspaceChangeViewModel(change);
                viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(WorkspaceChangeViewModel.IsSelected))
                    {
                        OnPropertyChanged(nameof(HasSelectedWorkspaceChanges));
                        OnPropertyChanged(nameof(WorkspaceSelectionText));
                        RaiseWorkspaceCommandStates();
                    }
                };
                WorkspaceChanges.Add(viewModel);
                if (viewModel.IsUntracked)
                {
                    UntrackedWorkspaceChanges.Add(viewModel);
                }
                else if (viewModel.IsStaged)
                {
                    StagedWorkspaceChanges.Add(viewModel);
                }
                else
                {
                    UnstagedWorkspaceChanges.Add(viewModel);
                }
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
            OnPropertyChanged(nameof(HasSelectedWorkspaceChanges));
            OnPropertyChanged(nameof(WorkspaceSelectionText));
            RaiseWorkspaceCommandStates();
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

    private async Task<WorkspaceRunSnapshot> CaptureWorkspaceSnapshotAsync(CancellationToken cancellationToken)
    {
        if (SelectedProject is null)
        {
            return new WorkspaceRunSnapshot("", 0, false);
        }

        try
        {
            var changeSet = await _workspaceChangeService.GetChangesAsync(
                SelectedProject.Path,
                maxFiles: 1_000,
                cancellationToken);
            return new WorkspaceRunSnapshot(
                changeSet.Branch,
                changeSet.Changes.Count,
                changeSet.IsTruncated);
        }
        catch
        {
            return new WorkspaceRunSnapshot(WorkspaceBranch, WorkspaceChanges.Count, false);
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
            var showStagedDiff = SelectedWorkspaceChange.IsStaged && !SelectedWorkspaceChange.HasUnstagedChanges;
            var diff = await _workspaceChangeService.GetDiffAsync(
                SelectedProject.Path,
                SelectedWorkspaceChange.Path,
                staged: showStagedDiff);
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

    private async Task RestoreSelectedWorkspaceFileAsync()
    {
        if (SelectedProject is null || SelectedWorkspaceChange is null)
        {
            return;
        }

        var change = SelectedWorkspaceChange;
        var message = change.IsUntracked
            ? $"删除未跟踪文件？\n\n{change.Path}"
            : $"恢复该文件的未提交改动？\n\n{change.Path}";
        var decision = System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            message,
            "确认恢复文件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var result = await _workspaceChangeService.RestoreFileAsync(
                SelectedProject.Path,
                change.Path,
                deleteUntracked: change.IsUntracked);
            StatusText = result.DeletedUntracked
                ? $"已删除未跟踪文件：{result.Path}"
                : $"已恢复文件：{result.Path}";
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"恢复失败：{ex.Message}";
            WorkspaceDiffText = $"恢复失败：{ex.Message}";
        }
    }

    private async Task RestoreSelectedWorkspaceChangesAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var changes = GetCheckedWorkspaceChanges();
        if (changes.Count == 0)
        {
            await RestoreSelectedWorkspaceFileAsync();
            return;
        }

        var decision = System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            $"恢复已选择的 {changes.Count} 个文件？\n\n未跟踪文件会被删除。",
            "确认恢复已选文件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        var restored = 0;
        var errors = new List<string>();
        foreach (var change in changes)
        {
            try
            {
                _ = await _workspaceChangeService.RestoreFileAsync(
                    SelectedProject.Path,
                    change.Path,
                    deleteUntracked: change.IsUntracked);
                restored++;
            }
            catch (Exception ex)
            {
                errors.Add($"{change.Path}: {ex.Message}");
            }
        }

        StatusText = errors.Count == 0
            ? $"已恢复 {restored} 个已选文件"
            : $"已恢复 {restored} 个文件，{errors.Count} 个失败";
        if (errors.Count > 0)
        {
            WorkspaceDiffText = string.Join(Environment.NewLine, errors);
        }

        await RefreshWorkspaceChangesAsync();
    }

    private async Task CommitSelectedWorkspaceFileAsync()
    {
        if (SelectedProject is null || SelectedWorkspaceChange is null)
        {
            return;
        }

        var change = SelectedWorkspaceChange;
        var defaultMessage = $"Update {System.IO.Path.GetFileName(change.Path)}";
        var message = TextPromptDialog.Show(
            System.Windows.Application.Current.MainWindow,
            "提交选中文件",
            defaultMessage);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var result = await _workspaceChangeService.CommitAsync(
                SelectedProject.Path,
                message,
                [change.Path]);
            StatusText = string.IsNullOrWhiteSpace(result.Commit)
                ? $"已提交：{result.Message}"
                : $"已提交 {result.Commit}：{result.Message}";
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"提交失败：{ex.Message}";
            WorkspaceDiffText = $"提交失败：{ex.Message}";
        }
    }

    private async Task CommitAllWorkspaceChangesAsync()
    {
        if (SelectedProject is null || !HasSelectedWorkspaceChanges)
        {
            return;
        }

        var changes = GetCheckedWorkspaceChanges();
        var paths = changes
            .Select(change => change.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var message = TextPromptDialog.Show(
            System.Windows.Application.Current.MainWindow,
            "提交已选工作区变更",
            $"Update {paths.Count} files");
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var result = await _workspaceChangeService.CommitAsync(
                SelectedProject.Path,
                message,
                paths);
            StatusText = string.IsNullOrWhiteSpace(result.Commit)
                ? $"已提交 {result.Paths.Count} 个文件：{result.Message}"
                : $"已提交 {result.Commit}：{result.Message}";
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"提交失败：{ex.Message}";
            WorkspaceDiffText = $"提交失败：{ex.Message}";
        }
    }

    private async Task StageSelectedWorkspaceChangesAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var paths = GetCheckedWorkspaceChanges()
            .Select(change => change.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            await _workspaceChangeService.StageAsync(SelectedProject.Path, paths);
            StatusText = $"已暂存 {paths.Count} 个文件";
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"暂存失败：{ex.Message}";
            WorkspaceDiffText = $"暂存失败：{ex.Message}";
        }
    }

    private async Task UnstageSelectedWorkspaceChangesAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var paths = GetCheckedWorkspaceChanges()
            .Where(change => change.IsStaged)
            .Select(change => change.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
        {
            StatusText = "请选择已暂存文件";
            return;
        }

        try
        {
            await _workspaceChangeService.UnstageAsync(SelectedProject.Path, paths);
            StatusText = $"已取消暂存 {paths.Count} 个文件";
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"取消暂存失败：{ex.Message}";
            WorkspaceDiffText = $"取消暂存失败：{ex.Message}";
        }
    }

    private void OpenWorkspaceFile()
    {
        if (SelectedWorkspaceChange is null)
        {
            return;
        }

        OpenProjectPath(SelectedWorkspaceChange.Path);
    }

    private void CopyWorkspaceDiff()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceDiffText))
        {
            return;
        }

        System.Windows.Clipboard.SetText(WorkspaceDiffText);
        StatusText = "当前 diff 已复制";
    }

    private void CopyWorkspacePath()
    {
        if (SelectedWorkspaceChange is null)
        {
            return;
        }

        System.Windows.Clipboard.SetText(SelectedWorkspaceChange.Path);
        StatusText = $"路径已复制：{SelectedWorkspaceChange.Path}";
    }

    private IReadOnlyList<WorkspaceChangeViewModel> GetCheckedWorkspaceChanges()
    {
        return WorkspaceChanges
            .Where(change => change.IsSelected)
            .ToList();
    }

    private void SetWorkspaceSelection(bool isSelected)
    {
        foreach (var change in WorkspaceChanges)
        {
            change.IsSelected = isSelected;
        }

        OnPropertyChanged(nameof(HasSelectedWorkspaceChanges));
        OnPropertyChanged(nameof(WorkspaceSelectionText));
        RaiseWorkspaceCommandStates();
    }

    private void RaiseWorkspaceCommandStates()
    {
        RefreshWorkspaceChangesCommand.RaiseCanExecuteChanged();
        RestoreWorkspaceFileCommand.RaiseCanExecuteChanged();
        CommitWorkspaceFileCommand.RaiseCanExecuteChanged();
        CommitAllWorkspaceChangesCommand.RaiseCanExecuteChanged();
        OpenWorkspaceFileCommand.RaiseCanExecuteChanged();
        CopyWorkspacePathCommand.RaiseCanExecuteChanged();
        StageSelectedWorkspaceChangesCommand.RaiseCanExecuteChanged();
        UnstageSelectedWorkspaceChangesCommand.RaiseCanExecuteChanged();
        SelectAllWorkspaceChangesCommand.RaiseCanExecuteChanged();
        ClearWorkspaceSelectionCommand.RaiseCanExecuteChanged();
    }

    private void OpenAgentFileChange(AgentFileChangeViewModel change)
    {
        OpenProjectPath(change.Path);
    }

    private void CopyAgentFileDiff(AgentFileChangeViewModel change)
    {
        if (!change.HasDiff)
        {
            return;
        }

        System.Windows.Clipboard.SetText(change.DiffText);
        StatusText = $"已复制 diff：{change.Path}";
    }

    private void CopyAgentFilePath(AgentFileChangeViewModel change)
    {
        System.Windows.Clipboard.SetText(change.Path);
        StatusText = $"路径已复制：{change.Path}";
    }

    private async Task CommitAgentRunChangesAsync(ChatMessageViewModel message)
    {
        if (SelectedProject is null || message.AgentRun is null)
        {
            return;
        }

        var paths = message.AgentRun.ChangedPaths;
        if (paths.Count == 0)
        {
            return;
        }

        var defaultMessage = $"Update agent changes ({paths.Count} files)";
        var commitMessage = TextPromptDialog.Show(
            System.Windows.Application.Current.MainWindow,
            "提交本轮变更",
            defaultMessage);
        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            return;
        }

        try
        {
            var result = await _workspaceChangeService.CommitAsync(
                SelectedProject.Path,
                commitMessage,
                paths);
            StatusText = string.IsNullOrWhiteSpace(result.Commit)
                ? $"已提交本轮 {result.Paths.Count} 个文件：{result.Message}"
                : $"已提交本轮 {result.Commit}：{result.Message}";
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"提交本轮失败：{ex.Message}";
            WorkspaceDiffText = $"提交本轮失败：{ex.Message}";
        }
    }

    private async Task RestoreAgentRunChangesAsync(ChatMessageViewModel message)
    {
        if (SelectedProject is null || message.AgentRun is null)
        {
            return;
        }

        var paths = message.AgentRun.ChangedPaths;
        if (paths.Count == 0)
        {
            return;
        }

        var decision = System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            $"撤销本轮记录的 {paths.Count} 个文件变更？\n\n这会恢复已跟踪文件，并删除本轮创建后仍未跟踪的文件。",
            "确认撤销本轮变更",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        var restored = 0;
        var errors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                _ = await _workspaceChangeService.RestoreFileAsync(
                    SelectedProject.Path,
                    path,
                    deleteUntracked: true);
                restored++;
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        StatusText = errors.Count == 0
            ? $"已撤销本轮 {restored} 个文件变更"
            : $"已撤销 {restored} 个文件，{errors.Count} 个失败";
        if (errors.Count > 0)
        {
            WorkspaceDiffText = string.Join(Environment.NewLine, errors);
        }

        await RefreshWorkspaceChangesAsync();
    }

    private void CopyAgentRunChangeSummary(ChatMessageViewModel message)
    {
        if (message.AgentRun is null || message.AgentRun.ChangedPaths.Count == 0)
        {
            return;
        }

        System.Windows.Clipboard.SetText(message.AgentRun.ChangeSummary);
        StatusText = "本轮变更摘要已复制";
    }

    private void CopySelectedAgentRunSummary()
    {
        if (SelectedAgentRunDetails is null)
        {
            return;
        }

        System.Windows.Clipboard.SetText(SelectedAgentRunDetails.RunSummary);
        StatusText = "Agent Run 摘要已复制";
    }

    private void CopySelectedAgentRunReviewPacket()
    {
        if (SelectedAgentRunDetails is null)
        {
            return;
        }

        System.Windows.Clipboard.SetText(SelectedAgentRunDetails.ReviewPacket);
        StatusText = "Agent Run 复盘包已复制";
    }

    private static bool CanOperateAgentRunChanges(object? parameter)
    {
        return parameter is ChatMessageViewModel { AgentRun.HasFileChanges: true };
    }

    private void OpenProjectPath(string relativePath)
    {
        if (SelectedProject is null || string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            var root = System.IO.Path.GetFullPath(SelectedProject.Path);
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relativePath));
            if (!fullPath.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "无法打开项目外路径";
                return;
            }

            var target = System.IO.File.Exists(fullPath) || System.IO.Directory.Exists(fullPath)
                ? fullPath
                : FindExistingParent(fullPath, root);
            var arguments = System.IO.File.Exists(target)
                ? $"/select,\"{target}\""
                : $"\"{target}\"";
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true
            });
            StatusText = $"已打开：{relativePath}";
        }
        catch (Exception ex)
        {
            StatusText = $"打开失败：{ex.Message}";
        }
    }

    private static string FindExistingParent(string fullPath, string root)
    {
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrWhiteSpace(directory) && directory.Length >= root.Length)
        {
            if (System.IO.Directory.Exists(directory))
            {
                return directory;
            }

            directory = System.IO.Path.GetDirectoryName(directory);
        }

        return root;
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
        NormalizeHarnessSettings();
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
        IsStopping = false;
        StatusText = "正在连接模型...";
        _sendCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var workspaceSnapshot = await CaptureWorkspaceSnapshotAsync(_sendCts.Token);
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
                                       WorkspaceBranch = workspaceSnapshot.Branch,
                                       WorkspaceChangeCountAtStart = workspaceSnapshot.ChangeCount,
                                       WorkspaceChangesWereTruncated = workspaceSnapshot.IsTruncated,
                                       Context = new AgentRunContext
                                       {
                                           ProjectPath = SelectedProject?.Path ?? Environment.CurrentDirectory,
                                           EnabledToolIds = Settings.EnabledToolIds,
                                           ToolPermissionModes = Settings.ToolPermissionModes,
                                           MaxToolRounds = Settings.AgentMaxToolRounds,
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
                                    RebuildAgentRunHistory();
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
                                assistantViewModel.SyncAgentVerifications();
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
                                    assistantViewModel.SyncAgentVerifications();
                                    assistantViewModel.AgentRun?.Complete(agentEvent.Run.Status);
                                    RebuildAgentRunHistory();
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
            var cancellationReason = IsStopping
                ? "用户手动停止生成。"
                : "请求超过 90 秒未完成。";
            if (!hasReceivedContent)
            {
                assistantViewModel.Content = "请求已停止，或模型长时间没有返回内容。";
                assistantViewModel.IsError = true;
            }

            StatusText = "已停止生成";
            assistantViewModel.AgentRun?.Complete(AgentRunStatus.Cancelled, cancellationReason);
            RebuildAgentRunHistory();
            await CompleteCallDetailAsync(callDetail, "已停止", new
            {
                status = "cancelled",
                reason = cancellationReason,
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
            assistantViewModel.AgentRun?.Complete(AgentRunStatus.Failed, ex.Message);
            RebuildAgentRunHistory();
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
            IsStopping = false;
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

    private void NormalizeHarnessSettings()
    {
        Settings.AgentMaxToolRounds = Math.Clamp(Settings.AgentMaxToolRounds, 1, 20);
        OnPropertyChanged(nameof(AgentMaxToolRounds));
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

    private sealed record WorkspaceRunSnapshot(string Branch, int ChangeCount, bool IsTruncated);

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
            AgentMaxToolRounds = Settings.AgentMaxToolRounds,
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
