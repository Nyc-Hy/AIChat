using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using AIChat.App.Controls;
using AIChat.Domain.Audit;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Context;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.Application.Agents;
using AIChat.Application.Audit;
using AIChat.Application.Context;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Projects;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Storage.Json;
using Ookii.Dialogs.Wpf;
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
    private readonly SimpleContextEstimator _fastContextEstimator = new();
    private readonly ConversationContextBuilder _contextBuilder;
    private readonly WorkspaceChangeService _workspaceChangeService;
    private readonly AuditLogRepository? _auditLogRepository;
    private AgentHarness? _agentHarness;
    private AgentToolRegistry? _toolRegistry;
    private readonly AgentRunQueue _agentRunQueue = new();
    private ProjectViewModel? _selectedProject;
    private ConversationViewModel? _selectedConversation;
    private AppSettings _settings = new();
    private string _draftMessage = "";
    private bool _isSending;
    private bool _isStopping;
    private string _statusText = "就绪";
    private string _agentStatusPhase = "";
    private string _agentStatusTool = "";
    private string _agentStatusBudget = "";
    private string _agentStatusPlan = "";
    private ContextUsage _contextUsage = new() { ModelLimit = 128_000, ConversationLimit = 64_000 };
    private CancellationTokenSource? _contextUsageCts;
    private int _contextUsageRevision;
    private readonly Dictionary<string, ContextUsage> _contextUsageCache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _settingsPersistCts;
    private CancellationTokenSource? _sendCts;
    private bool _isSettingsOpen;
    private bool _isCallDetailsOpen;
    private bool _isAgentRunHistoryOpen;
    private bool _isAgentRunDetailsOpen;
    private bool _isRemoveProjectConfirmationOpen;
    private ProjectViewModel? _projectPendingRemoval;
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
    // Tracks the provider template selected in the Settings "add provider" dropdown.
    // Separate from Settings.ProviderId to avoid async normalization races.
    private string _newProviderTemplateId = "tokenplan-mimo";
    // Guards against older async JSON loads overwriting a newer selection.
    private int _callDetailLoadVersion;
    private int _workspaceDiffLoadVersion;

    public MainViewModel(
        IAppRepository repository,
        IChatCompletionService chatService,
        IContextEstimator contextEstimator,
        ConversationContextBuilder contextBuilder,
        WorkspaceChangeService workspaceChangeService,
        AuditLogRepository? auditLogRepository = null)
    {
        _repository = repository;
        _chatService = chatService;
        _contextEstimator = contextEstimator;
        _contextBuilder = contextBuilder;
        _workspaceChangeService = workspaceChangeService;
        _auditLogRepository = auditLogRepository;
        // Commands are the bridge from XAML buttons/menu items to ViewModel methods.
        NewChatCommand = new RelayCommand(_ => NewChat(), _ => SelectedProject is not null && !IsSending);
        SendCommand = new RelayCommand(async _ => await SendAsync(), _ => CanSend);
        SelectProjectCommand = new RelayCommand(parameter => SelectProject((ProjectViewModel)parameter!));
        SelectConversationCommand = new RelayCommand(parameter => SelectConversation((ConversationViewModel)parameter!));
        LoadEarlierMessagesCommand = new RelayCommand(_ => LoadEarlierMessages(), _ => SelectedConversation?.HasHiddenMessages == true);
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
        OpenAgentRunHistoryCommand = new RelayCommand(_ => OpenAgentRunHistory(), _ => SelectedConversation is not null);
        CloseAgentRunHistoryCommand = new RelayCommand(_ => IsAgentRunHistoryOpen = false);
        SelectAgentRunHistoryItemCommand = new RelayCommand(parameter => SelectAgentRunHistoryItem((AgentRunHistoryItemViewModel)parameter!), parameter => parameter is AgentRunHistoryItemViewModel);
        RetryAgentRunCommand = new RelayCommand(parameter => RetryAgentRun((AgentRunHistoryItemViewModel)parameter!), parameter => parameter is AgentRunHistoryItemViewModel { CanRetry: true } && !IsSending);
        OpenAgentRunDetailsCommand = new RelayCommand(parameter => OpenAgentRunDetails((ChatMessageViewModel)parameter!), parameter => parameter is ChatMessageViewModel { HasAgentRun: true });
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
        ContinueAgentRunCommand = new RelayCommand(async parameter => await ContinueAgentRunAsync((AgentRunHistoryItemViewModel)parameter!), parameter => parameter is AgentRunHistoryItemViewModel { CanContinue: true } && !IsSending);
        ContinueSelectedAgentRunCommand = new RelayCommand(async _ => await ContinueSelectedAgentRunAsync(), _ => SelectedAgentRunDetails?.CanContinue == true && !IsSending);
        OpenAgentFileChangeCommand = new RelayCommand(parameter => OpenAgentFileChange((AgentFileChangeViewModel)parameter!), parameter => parameter is AgentFileChangeViewModel);
        CopyAgentFilePathCommand = new RelayCommand(parameter => CopyAgentFilePath((AgentFileChangeViewModel)parameter!), parameter => parameter is AgentFileChangeViewModel);
        CopyAgentFileDiffCommand = new RelayCommand(parameter => CopyAgentFileDiff((AgentFileChangeViewModel)parameter!), parameter => parameter is AgentFileChangeViewModel { HasDiff: true });
        CopyTraceCommand = new RelayCommand(parameter => CopyTrace((ToolTraceViewModel)parameter!), parameter => parameter is ToolTraceViewModel);
        ApproveToolCommand = new RelayCommand(_ => ResolvePendingToolApproval(allow: true, allowForSession: false), _ => PendingToolApproval is not null);
        ApproveToolForSessionCommand = new RelayCommand(_ => ResolvePendingToolApproval(allow: true, allowForSession: true), _ => PendingToolApproval is not null);
        RejectToolCommand = new RelayCommand(_ => ResolvePendingToolApproval(allow: false, allowForSession: false), _ => PendingToolApproval is not null);
        AddProjectToolOverrideCommand = new RelayCommand(_ => AddProjectToolOverride());
        RemoveProjectToolOverrideCommand = new RelayCommand(param => RemoveProjectToolOverride(param as string));
        AddProjectCommand = new RelayCommand(async _ => await AddProjectAsync());
        RemoveProjectCommand = new RelayCommand(param => OpenRemoveProjectConfirmation(param as ProjectViewModel), param => param is ProjectViewModel && Projects.Count > 1);
        ConfirmRemoveProjectCommand = new RelayCommand(async _ => await ConfirmRemoveProjectAsync(), _ => ProjectPendingRemoval is not null && Projects.Count > 1);
        CancelRemoveProjectCommand = new RelayCommand(_ => CloseRemoveProjectConfirmation());
    }

    public ObservableCollection<ProjectViewModel> Projects { get; } = [];
    public ObservableCollection<ToolOptionViewModel> ToolOptions { get; } = [];
    public ObservableCollection<ProjectToolPermissionOverrideViewModel> ProjectToolPermissionOverrides { get; } = [];
    public ObservableCollection<ModelParameterOptionViewModel> ModelParameterOptions { get; } = [];
    public ObservableCollection<WorkspaceChangeViewModel> WorkspaceChanges { get; } = [];
    public ObservableCollection<WorkspaceChangeViewModel> StagedWorkspaceChanges { get; } = [];
    public ObservableCollection<WorkspaceChangeViewModel> UnstagedWorkspaceChanges { get; } = [];
    public ObservableCollection<WorkspaceChangeViewModel> UntrackedWorkspaceChanges { get; } = [];
    public ObservableCollection<AgentRunHistoryItemViewModel> AgentRunHistory { get; } = [];
    public ObservableCollection<AuditEventViewModel> AuditEvents { get; } = [];
    public IReadOnlyList<SelectionOptionViewModel> AgentRunHistoryFilterOptions => AgentRunHistoryFilter.Options;
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
    public RelayCommand LoadEarlierMessagesCommand { get; }
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
    public RelayCommand ContinueAgentRunCommand { get; }
    public RelayCommand ContinueSelectedAgentRunCommand { get; }
    public RelayCommand OpenAgentFileChangeCommand { get; }
    public RelayCommand CopyAgentFilePathCommand { get; }
    public RelayCommand CopyAgentFileDiffCommand { get; }
    public RelayCommand CopyTraceCommand { get; }
    public RelayCommand ApproveToolCommand { get; }
    public RelayCommand ApproveToolForSessionCommand { get; }
    public RelayCommand RejectToolCommand { get; }
    public RelayCommand AddProjectToolOverrideCommand { get; }
    public RelayCommand RemoveProjectToolOverrideCommand { get; }
    public RelayCommand AddProjectCommand { get; }
    public RelayCommand RemoveProjectCommand { get; }
    public RelayCommand ConfirmRemoveProjectCommand { get; }
    public RelayCommand CancelRemoveProjectCommand { get; }

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
                OnPropertyChanged(nameof(RetryMaxAttempts));
                OnPropertyChanged(nameof(MaxOutputTokens));
                OnPropertyChanged(nameof(ConversationContextRatio));
                OnPropertyChanged(nameof(UseTokenizerEstimation));
                OnPropertyChanged(nameof(AuditLogMaxFileSizeMB));
                OnPropertyChanged(nameof(AuditLogRetentionDays));
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
                LoadProjectToolPermissionOverrides();
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
                OnPropertyChanged(nameof(AgentRunHistoryTitle));
                OnPropertyChanged(nameof(Messages));
                OnPropertyChanged(nameof(HasMessages));
                OnPropertyChanged(nameof(HasHiddenMessages));
                OnPropertyChanged(nameof(LoadEarlierMessagesText));
                LoadEarlierMessagesCommand.RaiseCanExecuteChanged();
                OpenAgentRunHistoryCommand.RaiseCanExecuteChanged();
                UpdateContextUsage();
                RebuildAgentRunHistoryIfOpen();
            }
        }
    }

    public ObservableCollection<ChatMessageViewModel>? Messages => SelectedConversation?.Messages;
    public bool HasMessages => Messages?.Count > 0;
    public bool HasHiddenMessages => SelectedConversation?.HasHiddenMessages == true;
    public string LoadEarlierMessagesText => SelectedConversation?.LoadEarlierMessagesText ?? "";
    public string CurrentProjectName => SelectedProject?.Name ?? "未选择项目";
    public string WindowTitle
    {
        get
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version is not null ? $" v{version.Major}.{version.Minor}.{version.Build}" : "";
            var project = SelectedProject?.Name;
            return string.IsNullOrEmpty(project) ? $"AIChat{versionStr}" : $"AIChat{versionStr} — {project}";
        }
    }
    public string CurrentConversationTitle => SelectedConversation?.Title ?? "新对话";
    public string ModelName => SelectedConfiguredProvider is null
        ? "未配置模型"
        : $"{SelectedConfiguredProvider.Name} · {SelectedConfiguredProvider.SelectedModelId}";
    public IReadOnlyList<ConfiguredLlmProvider> ConfiguredProviders => Settings.ConfiguredProviders;
    public ConfiguredLlmProvider? SelectedConfiguredProvider =>
        Settings.ConfiguredProviders.FirstOrDefault(provider => provider.Id == Settings.ActiveConfiguredProviderId) ??
        Settings.ConfiguredProviders.FirstOrDefault();
    public IReadOnlyList<ModelOptionItem> ActiveModelOptions
    {
        get
        {
            var items = new List<ModelOptionItem>();
            var selectedProviderId = Settings.ActiveConfiguredProviderId;
            foreach (var configured in Settings.ConfiguredProviders)
            {
                if (string.IsNullOrWhiteSpace(configured.ApiKey))
                {
                    continue;
                }

                // Filter by selected provider
                if (!string.IsNullOrEmpty(selectedProviderId) && configured.Id != selectedProviderId)
                {
                    continue;
                }

                var template = ChatProviderCatalog.Resolve(configured.TemplateId);
                foreach (var model in template.Models)
                {
                    items.Add(new ModelOptionItem(
                        $"{configured.TemplateId}|{model.Id}",
                        $"[{template.Name}] {model.DisplayName}"));
                }
            }

            return items;
        }
    }
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
    public bool ActiveModelSupportsTools
    {
        get
        {
            var configured = SelectedConfiguredProvider;
            if (configured is null) return false;
            var model = ChatProviderCatalog.ResolveModel(configured.TemplateId, configured.SelectedModelId);
            return model.Capabilities?.SupportsTools == true;
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
            RemoveConfiguredProviderCommand.RaiseCanExecuteChanged();
            _ = PersistSettingsQuietlyAsync();
        }
    }

    public string SelectedActiveModelId
    {
        get
        {
            var configured = SelectedConfiguredProvider;
            return configured is null ? "" : $"{configured.TemplateId}|{configured.SelectedModelId}";
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var parts = value.Split('|', 2);
            if (parts.Length != 2)
            {
                return;
            }

            var templateId = parts[0];
            var modelId = parts[1];

            // If the model belongs to a different provider, switch to it.
            var configured = Settings.ConfiguredProviders.FirstOrDefault(
                p => string.Equals(p.TemplateId, templateId, StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(p.ApiKey));
            if (configured is null)
            {
                return;
            }

            if (!string.Equals(Settings.ActiveConfiguredProviderId, configured.Id, StringComparison.Ordinal))
            {
                Settings.ActiveConfiguredProviderId = configured.Id;
            }

            var model = ChatProviderCatalog.ResolveModel(templateId, modelId);
            configured.SelectedModelId = model.Id;
            configured.ModelParameters = NormalizeModelParameterValues(templateId, model.Id, configured.ModelParameters);
            ApplySelectedConfiguredProvider();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(ModelName));
            OnPropertyChanged(nameof(SelectedConfiguredProvider));
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

    public string NewProviderTemplateId
    {
        get => _newProviderTemplateId;
        set
        {
            if (SetProperty(ref _newProviderTemplateId, value))
            {
                // Also sync to SelectedProviderId for backward compatibility.
                SelectedProviderId = value;
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

    public bool IsRemoveProjectConfirmationOpen
    {
        get => _isRemoveProjectConfirmationOpen;
        private set => SetProperty(ref _isRemoveProjectConfirmationOpen, value);
    }

    public ProjectViewModel? ProjectPendingRemoval
    {
        get => _projectPendingRemoval;
        private set
        {
            if (SetProperty(ref _projectPendingRemoval, value))
            {
                OnPropertyChanged(nameof(ProjectRemovalName));
                OnPropertyChanged(nameof(ProjectRemovalPathText));
                ConfirmRemoveProjectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProjectRemovalName => ProjectPendingRemoval?.Name ?? "";

    public string ProjectRemovalPathText => string.IsNullOrWhiteSpace(ProjectPendingRemoval?.Path)
        ? "未设置项目路径"
        : ProjectPendingRemoval.Path;

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
                RebuildAgentRunHistoryIfOpen();
            }
        }
    }
    public string AgentRunHistoryTitle => SelectedConversation is null
        ? "运行历史"
        : $"{SelectedConversation.Title} · 运行历史";
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
                ContinueSelectedAgentRunCommand.RaiseCanExecuteChanged();
                _ = LoadAuditEventsAsync(value);
            }
        }
    }

    public string AgentRunDetailsTitle => SelectedAgentRunDetails is null
        ? "Agent Run"
        : $"Agent Run · {SelectedAgentRunDetails.StatusText}";

    public bool HasAuditEvents => AuditEvents.Count > 0;

    private async Task LoadAuditEventsAsync(AgentRunViewModel? run)
    {
        await InvokeOnUiAsync(() =>
        {
            AuditEvents.Clear();
            OnPropertyChanged(nameof(HasAuditEvents));
        });

        var projectId = SelectedProject?.Project.Id ?? "";
        var runId = run?.Id ?? "";
        if (run is null || _auditLogRepository is null || string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        try
        {
            var items = await AgentRunAuditLoader.LoadAsync(
                _auditLogRepository, projectId, runId, run.Run.StartedAt);

            if (SelectedAgentRunDetails?.Id != runId)
            {
                return;
            }

            await InvokeOnUiAsync(() =>
            {
                foreach (var e in items)
                {
                    AuditEvents.Add(new AuditEventViewModel(e));
                }

                OnPropertyChanged(nameof(HasAuditEvents));
            });
        }
        catch
        {
            // Audit display is best-effort; don't break the UI.
        }
    }

    private static Task InvokeOnUiAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
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
                RetryAgentRunCommand.RaiseCanExecuteChanged();
                RetrySelectedAgentRunCommand.RaiseCanExecuteChanged();
                ContinueAgentRunCommand.RaiseCanExecuteChanged();
                ContinueSelectedAgentRunCommand.RaiseCanExecuteChanged();
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

    public string AgentStatusPhase
    {
        get => _agentStatusPhase;
        private set => SetProperty(ref _agentStatusPhase, value);
    }

    public string AgentStatusTool
    {
        get => _agentStatusTool;
        private set => SetProperty(ref _agentStatusTool, value);
    }

    public string AgentStatusBudget
    {
        get => _agentStatusBudget;
        private set => SetProperty(ref _agentStatusBudget, value);
    }

    public string AgentStatusPlan
    {
        get => _agentStatusPlan;
        private set => SetProperty(ref _agentStatusPlan, value);
    }

    public bool HasAgentStatus => IsSending && (!string.IsNullOrEmpty(AgentStatusPhase) ||
                                                !string.IsNullOrEmpty(AgentStatusTool) ||
                                                !string.IsNullOrEmpty(AgentStatusBudget));

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
            var normalized = Math.Clamp(value, 1, 100);
            if (Settings.AgentMaxToolRounds == normalized)
            {
                return;
            }

            Settings.AgentMaxToolRounds = normalized;
            OnPropertyChanged();
        }
    }

    public int RetryMaxAttempts
    {
        get => Settings.RetryMaxAttempts;
        set
        {
            var normalized = Math.Clamp(value, 0, 10);
            if (Settings.RetryMaxAttempts == normalized) return;
            Settings.RetryMaxAttempts = normalized;
            OnPropertyChanged();
        }
    }

    public int MaxOutputTokens
    {
        get => Settings.MaxOutputTokens;
        set
        {
            var normalized = Math.Clamp(value, 256, 32768);
            if (Settings.MaxOutputTokens == normalized) return;
            Settings.MaxOutputTokens = normalized;
            OnPropertyChanged();
        }
    }

    public double ConversationContextRatio
    {
        get => Settings.ConversationContextRatio;
        set
        {
            var normalized = Math.Clamp(value, 0.3, 1.0);
            if (Math.Abs(Settings.ConversationContextRatio - normalized) < 0.01) return;
            Settings.ConversationContextRatio = normalized;
            OnPropertyChanged();
        }
    }

    public bool UseTokenizerEstimation
    {
        get => Settings.UseTokenizerEstimation;
        set
        {
            if (Settings.UseTokenizerEstimation == value) return;
            Settings.UseTokenizerEstimation = value;
            OnPropertyChanged();
        }
    }

    public long AuditLogMaxFileSizeMB
    {
        get => Settings.AuditLogMaxFileSizeBytes / (1024 * 1024);
        set
        {
            var bytes = Math.Max(1, value) * 1024 * 1024;
            if (Settings.AuditLogMaxFileSizeBytes == bytes) return;
            Settings.AuditLogMaxFileSizeBytes = bytes;
            OnPropertyChanged();
        }
    }

    public int AuditLogRetentionDays
    {
        get => Settings.AuditLogRetentionDays;
        set
        {
            var normalized = Math.Clamp(value, 1, 365);
            if (Settings.AuditLogRetentionDays == normalized) return;
            Settings.AuditLogRetentionDays = normalized;
            OnPropertyChanged();
        }
    }

    private bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(DraftMessage) && SelectedProject is not null;

    public void ConfigureAgent(AgentHarness agentHarness, AgentToolRegistry toolRegistry)
    {
        _agentHarness = agentHarness;
        _toolRegistry = toolRegistry;
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
        // Reject any pending tool approval before cancelling the run
        if (PendingToolApproval is not null)
        {
            PendingToolApproval.Reject();
        }
        _sendCts.Cancel();
    }

    public async Task InitializeAsync()
    {
        // Startup sequence: load settings, normalize old values, load projects,
        // then select the default project/conversation for the UI.
        Settings = await _repository.LoadSettingsAsync();
        NormalizeProviderSettings();
        _newProviderTemplateId = Settings.ProviderId;
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

        // Restore last active project, or fall back to the first one.
        var targetProject = Projects.FirstOrDefault(
                                p => string.Equals(p.Project.Id, Settings.LastActiveProjectId, StringComparison.Ordinal)) ??
                            Projects.FirstOrDefault();
        SelectProject(targetProject);

        // Restore last active conversation within the selected project.
        if (SelectedProject is not null && !string.IsNullOrWhiteSpace(Settings.LastActiveConversationId))
        {
            var lastConversation = SelectedProject.Conversations.FirstOrDefault(
                c => string.Equals(c.Conversation.Id, Settings.LastActiveConversationId, StringComparison.Ordinal));
            if (lastConversation is not null)
            {
                SelectConversation(lastConversation);
            }
        }

        // If the selected project has no path configured, prompt the user.
        if (SelectedProject is not null && string.IsNullOrWhiteSpace(SelectedProject.Path))
        {
            PromptForProjectPath();
        }

        // Workspace changes are loaded on-demand via RefreshWorkspaceChangesCommand
        // when the user switches to the "文件变更" tab, to avoid blocking startup.
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
        SaveProjectToolPermissionOverrides();
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

        if (ReferenceEquals(SelectedProject, project))
        {
            return;
        }

        if (SelectedProject is not null)
        {
            SelectedProject.IsSelected = false;
        }

        project.IsSelected = true;
        SelectedProject = project;
        Settings.LastActiveProjectId = project.Project.Id;
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

    private void PromptForProjectPath()
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = $"请选择 \"{SelectedProject!.Name}\" 的项目文件夹",
            ShowNewFolderButton = false,
            RootFolder = Environment.SpecialFolder.MyComputer
        };

        if (dialog.ShowDialog() == true && Directory.Exists(dialog.SelectedPath))
        {
            SelectedProject.Project.Path = dialog.SelectedPath;
            _ = _repository.SaveProjectsAsync(Projects.Select(p => p.Project).ToList());
            OnPropertyChanged(nameof(SelectedProject));
            StatusText = $"项目路径已设置为：{dialog.SelectedPath}";
        }
        else
        {
            StatusText = "未设置项目路径，工具将以应用目录为根路径运行。请稍后通过「添加项目」配置正确路径。";
        }
    }

    private async Task AddProjectAsync()
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = "选择项目文件夹",
            ShowNewFolderButton = false,
            RootFolder = Environment.SpecialFolder.MyComputer
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var folderPath = dialog.SelectedPath;
        if (!Directory.Exists(folderPath))
        {
            return;
        }

        // Check for duplicates
        if (Projects.Any(project => string.Equals(project.Path, folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            // Already added — just select it
            SelectProject(Projects.First(project => string.Equals(project.Path, folderPath, StringComparison.OrdinalIgnoreCase)));
            return;
        }

        var projectName = Path.GetFileName(folderPath);
        var workspace = new ProjectWorkspace
        {
            Name = projectName,
            Path = folderPath,
            UpdatedAt = DateTimeOffset.Now
        };

        var projectVm = new ProjectViewModel(workspace);
        Projects.Add(projectVm);
        await _repository.SaveProjectsAsync(Projects.Select(project => project.Project).ToList());
        SelectProject(projectVm);

        // Initialize project (generate AGENTS.md if missing)
        try
        {
            var initializer = new ProjectInitializer();
            await initializer.InitializeProjectAsync(folderPath);
        }
        catch
        {
            // Non-fatal — project still usable without AGENTS.md
        }
    }

    private void OpenRemoveProjectConfirmation(ProjectViewModel? project)
    {
        if (project is null || Projects.Count <= 1)
        {
            return;
        }

        ProjectPendingRemoval = project;
        IsRemoveProjectConfirmationOpen = true;
    }

    private void CloseRemoveProjectConfirmation()
    {
        IsRemoveProjectConfirmationOpen = false;
        ProjectPendingRemoval = null;
    }

    private async Task ConfirmRemoveProjectAsync()
    {
        var project = ProjectPendingRemoval;
        if (project is null || Projects.Count <= 1)
        {
            return;
        }

        var wasSelected = project.IsSelected;
        Projects.Remove(project);
        CloseRemoveProjectConfirmation();

        if (wasSelected)
        {
            SelectProject(Projects.FirstOrDefault());
        }

        await _repository.SaveProjectsAsync(Projects.Select(project => project.Project).ToList());
        StatusText = "项目已移除";
        RemoveProjectCommand.RaiseCanExecuteChanged();
    }

    private void SelectConversation(ConversationViewModel conversation)
    {
        if (SelectedProject is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedConversation, conversation))
        {
            return;
        }

        if (SelectedConversation is not null)
        {
            SelectedConversation.IsSelected = false;
        }

        conversation.IsSelected = true;
        SelectedConversation = conversation;
        Settings.LastActiveConversationId = conversation.Conversation.Id;
        QueueSettingsPersist();
    }

    private void QueueSettingsPersist()
    {
        _settingsPersistCts?.Cancel();
        _settingsPersistCts?.Dispose();
        var cts = new CancellationTokenSource();
        _settingsPersistCts = cts;
        _ = PersistSettingsAfterDelayAsync(cts.Token);
    }

    private async Task PersistSettingsAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(400, cancellationToken);
            await PersistSettingsQuietlyAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void LoadEarlierMessages()
    {
        SelectedConversation?.LoadEarlierMessages();
        OnPropertyChanged(nameof(HasHiddenMessages));
        OnPropertyChanged(nameof(LoadEarlierMessagesText));
        OnPropertyChanged(nameof(HasMessages));
        LoadEarlierMessagesCommand.RaiseCanExecuteChanged();
    }

    private void RebuildAgentRunHistory()
    {
        AgentRunHistory.Clear();
        if (SelectedConversation is null)
        {
            _agentRunHistoryTotalCount = 0;
            RaiseAgentRunHistoryProperties();
            return;
        }

        var allItems = AgentRunHistoryFilter.GatherFromConversation(SelectedConversation);

        _agentRunHistoryTotalCount = allItems.Count;
        var items = FilterAgentRunHistory(allItems).ToList();

        foreach (var item in items)
        {
            AgentRunHistory.Add(item);
        }

        RaiseAgentRunHistoryProperties();
    }

    private void RebuildAgentRunHistoryIfOpen()
    {
        if (IsAgentRunHistoryOpen)
        {
            RebuildAgentRunHistory();
        }
    }

    private void RaiseAgentRunHistoryProperties()
    {
        OnPropertyChanged(nameof(HasAgentRunHistory));
        OnPropertyChanged(nameof(AgentRunHistorySummary));
        RetryAgentRunCommand.RaiseCanExecuteChanged();
        ContinueAgentRunCommand.RaiseCanExecuteChanged();
    }

    private IEnumerable<AgentRunHistoryItemViewModel> FilterAgentRunHistory(IEnumerable<AgentRunHistoryItemViewModel> items)
    {
        return AgentRunHistoryFilter.Apply(items, AgentRunHistoryFilterId);
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

    private async Task ContinueAgentRunAsync(AgentRunHistoryItemViewModel item)
    {
        if (!item.CanContinue || IsSending)
        {
            return;
        }

        SelectConversation(item.Conversation);
        IsAgentRunHistoryOpen = false;
        IsAgentRunDetailsOpen = false;
        DraftMessage = item.Run.RecoverySuggestion;
        _pendingContinuedFromRunId = item.Run.Id;
        await SendAsync();
    }

    private async Task ContinueSelectedAgentRunAsync()
    {
        var selected = SelectedAgentRunDetails;
        if (selected is null || !selected.CanContinue || IsSending)
        {
            return;
        }

        var historyItem = AgentRunHistory.FirstOrDefault(item => item.Run.Id == selected.Id);
        if (historyItem is not null)
        {
            await ContinueAgentRunAsync(historyItem);
            return;
        }

        IsAgentRunDetailsOpen = false;
        DraftMessage = selected.RecoverySuggestion;
        _pendingContinuedFromRunId = selected.Id;
        await SendAsync();
    }

    private string _pendingContinuedFromRunId = "";

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
            var result = WorkspaceChangeListBuilder.Build(changeSet);

            WorkspaceChanges.Clear();
            StagedWorkspaceChanges.Clear();
            UnstagedWorkspaceChanges.Clear();
            UntrackedWorkspaceChanges.Clear();
            foreach (var viewModel in result.All)
            {
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
            }
            foreach (var vm in result.Staged) StagedWorkspaceChanges.Add(vm);
            foreach (var vm in result.Unstaged) UnstagedWorkspaceChanges.Add(vm);
            foreach (var vm in result.Untracked) UntrackedWorkspaceChanges.Add(vm);

            WorkspaceBranch = result.Branch;
            WorkspaceStatusText = result.StatusText;
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
            var showStagedDiff = WorkspaceDiffFormatter.ShouldShowStagedDiff(
                SelectedWorkspaceChange.IsStaged,
                SelectedWorkspaceChange.HasUnstagedChanges);
            var diff = await _workspaceChangeService.GetDiffAsync(
                SelectedProject.Path,
                SelectedWorkspaceChange.Path,
                staged: showStagedDiff);
            if (version != _workspaceDiffLoadVersion)
            {
                return;
            }

            WorkspaceDiffText = WorkspaceDiffFormatter.FormatDiffText(diff);
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
        var message = WorkspaceOperationTextFormatter.RestoreSingleFileConfirm(change.IsUntracked, change.Path);
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
            StatusText = WorkspaceOperationTextFormatter.RestoreSingleFileSuccess(result.DeletedUntracked, result.Path);
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = WorkspaceOperationTextFormatter.RestoreError(ex.Message);
            WorkspaceDiffText = WorkspaceOperationTextFormatter.RestoreError(ex.Message);
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
            WorkspaceOperationTextFormatter.RestoreSelectedConfirm(changes.Count),
            "确认恢复已选文件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await WorkspaceRestoreBatchRunner.RestoreAsync(
            _workspaceChangeService, SelectedProject.Path,
            changes.Select(c => c.Change).ToList());

        StatusText = WorkspaceOperationTextFormatter.RestoreMultipleSuccess(result.Restored, result.Errors.Count);
        if (result.Errors.Count > 0)
        {
            WorkspaceDiffText = string.Join(Environment.NewLine, result.Errors);
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
        var defaultMessage = WorkspaceOperationTextFormatter.CommitSingleFileDefaultMessage(change.Path);
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
            var result = await WorkspaceCommitBatchRunner.CommitAsync(
                _workspaceChangeService, SelectedProject.Path,
                message, change.Path);
            StatusText = WorkspaceOperationTextFormatter.CommitSingleFileSuccess(result);
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = WorkspaceOperationTextFormatter.CommitError(ex.Message);
            WorkspaceDiffText = WorkspaceOperationTextFormatter.CommitError(ex.Message);
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
            WorkspaceOperationTextFormatter.CommitMultipleDefaultMessage(paths.Count));
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var result = await WorkspaceCommitBatchRunner.CommitAsync(
                _workspaceChangeService, SelectedProject.Path,
                message, changes.Select(c => c.Change).ToList());
            StatusText = WorkspaceOperationTextFormatter.CommitMultipleSuccess(result);
            await RefreshWorkspaceChangesAsync();
        }
        catch (Exception ex)
        {
            StatusText = WorkspaceOperationTextFormatter.CommitError(ex.Message);
            WorkspaceDiffText = WorkspaceOperationTextFormatter.CommitError(ex.Message);
        }
    }

    private async Task StageSelectedWorkspaceChangesAsync()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var changes = GetCheckedWorkspaceChanges();
        if (changes.Count == 0)
        {
            return;
        }

        try
        {
            var result = await WorkspaceStageBatchRunner.StageAsync(
                _workspaceChangeService, SelectedProject.Path,
                changes.Select(c => c.Change).ToList());
            StatusText = $"已暂存 {result.Count} 个文件";
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

        var changes = GetCheckedWorkspaceChanges()
            .Where(c => c.IsStaged)
            .ToList();
        if (changes.Count == 0)
        {
            StatusText = "请选择已暂存文件";
            return;
        }

        try
        {
            var result = await WorkspaceStageBatchRunner.UnstageAsync(
                _workspaceChangeService, SelectedProject.Path,
                changes.Select(c => c.Change).ToList());
            StatusText = $"已取消暂存 {result.Count} 个文件";
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

    private void CopyTrace(ToolTraceViewModel trace)
    {
        System.Windows.Clipboard.SetText(trace.GetFullText());
        StatusText = $"工具调用详情已复制：{trace.ToolName}";
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

        var fileChanges = message.AgentRun.FileChanges.ToList();
        if (fileChanges.Count == 0)
        {
            return;
        }

        // Detect conflicts: files that were manually edited after the Agent run
        var conflicts = new List<string>();
        foreach (var change in fileChanges)
        {
            if (string.IsNullOrEmpty(change.PostChangeHash))
            {
                continue;
            }

            var fullPath = System.IO.Path.Combine(SelectedProject.Path, change.Path);
            if (!System.IO.File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                var currentContent = await System.IO.File.ReadAllTextAsync(fullPath);
                var currentHash = ComputeContentHash(currentContent);
                if (currentHash != change.PostChangeHash)
                {
                    conflicts.Add(change.Path);
                }
            }
            catch
            {
                // Can't read file, skip conflict check for this file
            }
        }

        var confirmMessage = conflicts.Count > 0
            ? $"以下 {conflicts.Count} 个文件在 Agent 修改后又被手动编辑过：\n\n{string.Join("\n", conflicts.Select(p => "  - " + p))}\n\n仍要撤销这些文件的变更吗？手动编辑的内容将丢失。"
            : $"撤销本轮记录的 {fileChanges.Count} 个文件变更？\n\n这会恢复已跟踪文件，并删除本轮创建后仍未跟踪的文件。";

        var decision = System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            confirmMessage,
            "确认撤销本轮变更",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        var restored = 0;
        var errors = new List<string>();
        foreach (var change in fileChanges)
        {
            try
            {
                var result = await _workspaceChangeService.RestoreFileAsync(
                    SelectedProject.Path,
                    change.Path,
                    deleteUntracked: true);
                restored++;
            }
            catch
            {
                // git restore failed — try snapshot-based fallback for untracked files
                if (!string.IsNullOrEmpty(change.ContentSnapshot))
                {
                    try
                    {
                        var fullPath = System.IO.Path.Combine(SelectedProject.Path, change.Path);
                        var directory = System.IO.Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrWhiteSpace(directory) && !System.IO.Directory.Exists(directory))
                        {
                            System.IO.Directory.CreateDirectory(directory);
                        }

                        await System.IO.File.WriteAllTextAsync(fullPath, change.ContentSnapshot);
                        restored++;
                    }
                    catch (Exception ex2)
                    {
                        errors.Add($"{change.Path}: {ex2.Message}");
                    }
                }
                else
                {
                    errors.Add($"{change.Path}: 无法恢复");
                }
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

    private static string ComputeContentHash(string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
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

        // Ensure project path is configured before sending.
        if (SelectedProject is not null && string.IsNullOrWhiteSpace(SelectedProject.Path))
        {
            PromptForProjectPath();
            if (string.IsNullOrWhiteSpace(SelectedProject.Path))
            {
                return;
            }
        }

        var text = DraftMessage.Trim();
        DraftMessage = "";
        var continuedFromRunId = _pendingContinuedFromRunId;
        _pendingContinuedFromRunId = "";
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
        var reasoningContentBuilder = new System.Text.StringBuilder();
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

        if (!_agentRunQueue.TryStart(assistantMessage.Id))
        {
            StatusText = "已有任务运行中，请等待完成后再试";
            return;
        }

        IsSending = true;
        IsStopping = false;
        StatusText = "正在连接模型...";
        _sendCts = new CancellationTokenSource();
        var workspaceSnapshot = await CaptureWorkspaceSnapshotAsync(_sendCts.Token);
        var rawResponseEvents = new List<string>();
        var toolTraceByCallId = new Dictionary<string, ToolTraceViewModel>(StringComparer.Ordinal);
        var stepByToolCallId = new Dictionary<string, AgentStepViewModel>(StringComparer.Ordinal);

        var projectPath = string.IsNullOrWhiteSpace(SelectedProject?.Path) ? Environment.CurrentDirectory : SelectedProject!.Path;
        var fileIndex = new ProjectFileIndexBuilder().Build(projectPath);
        var workspaceSummary = workspaceSnapshot is { Branch: { Length: > 0 } branch }
            ? $"分支：{branch}，未提交变更：{workspaceSnapshot.ChangeCount} 个文件"
            : "";
        var pinnedItems = SelectedProject?.Project.PinnedContext ?? [];

        var contextMessages = _contextBuilder.Build(new ConversationContextBuildRequest
        {
            Messages = SelectedConversation.Conversation.Messages
                .Where(message => message.Id != assistantMessage.Id && !string.IsNullOrWhiteSpace(message.Content))
                .ToList(),
            Settings = effectiveSettings,
            PromptContext = new SystemPromptContext
            {
                ProviderId = effectiveSettings.ProviderId,
                ProjectName = SelectedProject?.Name ?? "AIChat",
                ProjectPath = projectPath,
                EnabledToolIds = Settings.EnabledToolIds,
                ToolPermissionModes = Settings.ToolPermissionModes,
                FileIndex = fileIndex,
                WorkspaceSummary = workspaceSummary,
                PinnedContextItems = pinnedItems
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
                var modelInfo = ChatProviderCatalog.ResolveModel(
                    effectiveSettings.ActiveConfiguredProviderId,
                    effectiveSettings.Model);
                var supportsTools = modelInfo?.Capabilities?.SupportsTools == true;

                if (_agentHarness is null || !supportsTools)
                {
                    await foreach (var delta in _chatService.SendAsync(request, effectiveSettings, _sendCts.Token))
                    {
                        // Preserve raw protocol events separately from rendered content.
                        if (!string.IsNullOrWhiteSpace(delta.RawJson))
                        {
                            rawResponseEvents.Add(delta.RawJson);
                        }

                        if (!string.IsNullOrEmpty(delta.ReasoningContent))
                        {
                            reasoningContentBuilder.Append(delta.ReasoningContent);
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

                    if (_agentHarness is not null && !supportsTools)
                    {
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            StatusText = "当前模型不支持工具调用，已回退到普通聊天模式";
                        });
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
                                       ContinuedFromRunId = continuedFromRunId,
                                       Context = new AgentRunContext
                                       {
                                           ProjectPath = string.IsNullOrWhiteSpace(SelectedProject?.Path) ? Environment.CurrentDirectory : SelectedProject!.Path,
                                           EnabledToolIds = Settings.EnabledToolIds,
                                           ToolPermissionModes = MergeToolPermissionModes(
                                               Settings.ToolPermissionModes,
                                               SelectedProject?.Project.ProjectToolPermissionModes),
                                           MaxToolRounds = Settings.AgentMaxToolRounds,
                                           RequestToolApprovalAsync = RequestToolApprovalAsync,
                                           AutoVerifyAgentRuns = Settings.AutoVerifyAgentRuns,
                                           MaxAutoFixRounds = Settings.MaxAutoFixRounds,
                                           VerificationCommands = SelectedProject?.Project.VerificationCommands ?? []
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
                                    RebuildAgentRunHistoryIfOpen();
                                }
                                AgentStatusPhase = "正在规划";
                                OnPropertyChanged(nameof(HasAgentStatus));
                            });
                            await RecordAuditEventAsync(AuditEventType.AgentRunStarted,
                                SelectedProject?.Project.Id ?? "", agentEvent.Run?.Id ?? "",
                                summary: text);
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
                                    AgentStatusPhase = "正在回复";
                                    AgentStatusTool = "";
                                    OnPropertyChanged(nameof(HasAgentStatus));
                                });
                            }

                            await AppendAssistantContentAsync(assistantViewModel, agentEvent.Content, _sendCts.Token);
                            break;
                        case AgentHarnessEventType.ToolCall:
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                StatusText = $"调用工具：{agentEvent.ToolCall?.Name}";
                                AgentStatusPhase = "正在执行";
                                AgentStatusTool = agentEvent.ToolCall?.Name ?? "";
                                OnPropertyChanged(nameof(HasAgentStatus));
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
                            await RecordAuditEventAsync(AuditEventType.ToolCallRequested,
                                SelectedProject?.Project.Id ?? "", agentEvent.Run?.Id ?? "",
                                toolName: agentEvent.ToolCall?.Name ?? "",
                                summary: $"Tool call: {agentEvent.ToolCall?.Name}",
                                detail: agentEvent.ToolCall?.ArgumentsJson ?? "");
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
                                AgentStatusPhase = "等待审批";
                                AgentStatusTool = agentEvent.ToolCall?.Name ?? "";
                                OnPropertyChanged(nameof(HasAgentStatus));
                            });
                            break;
                        case AgentHarnessEventType.ToolApprovalRejected:
                            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                StatusText = $"已拒绝工具：{agentEvent.ToolCall?.Name}";
                            });
                            await RecordAuditEventAsync(AuditEventType.ToolCallRejected,
                                SelectedProject?.Project.Id ?? "", agentEvent.Run?.Id ?? "",
                                toolName: agentEvent.ToolCall?.Name ?? "",
                                summary: $"Rejected: {agentEvent.ToolCall?.Name}");
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
                                assistantViewModel.SyncAgentPlan();
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
                                    RebuildAgentRunHistoryIfOpen();
                                }

                                AgentStatusPhase = agentEvent.Run?.Status == AgentRunStatus.Completed ? "已完成" : "已结束";
                                AgentStatusTool = "";
                                OnPropertyChanged(nameof(HasAgentStatus));
                            });
                            {
                                var runStatus = agentEvent.Run?.Status;
                                var auditType = runStatus switch
                                {
                                    AgentRunStatus.Completed => AuditEventType.AgentRunCompleted,
                                    AgentRunStatus.Failed => AuditEventType.AgentRunFailed,
                                    AgentRunStatus.Cancelled => AuditEventType.AgentRunCancelled,
                                    _ => AuditEventType.AgentRunCompleted
                                };
                                await RecordAuditEventAsync(auditType,
                                    SelectedProject?.Project.Id ?? "", agentEvent.Run?.Id ?? "",
                                    summary: $"Run {runStatus}");
                            }
                            break;
                    }
                }
            }, _sendCts.Token);

            // Store reasoning content for DeepSeek thinking mode replay.
            if (reasoningContentBuilder.Length > 0)
            {
                assistantMessage.ReasoningContent = reasoningContentBuilder.ToString();
            }

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
            RebuildAgentRunHistoryIfOpen();
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
            RebuildAgentRunHistoryIfOpen();
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
            _agentRunQueue.Complete(assistantMessage.Id);
            IsSending = false;
            IsStopping = false;
            AgentStatusPhase = "";
            AgentStatusTool = "";
            AgentStatusBudget = "";
            AgentStatusPlan = "";
            OnPropertyChanged(nameof(HasAgentStatus));
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
            // Wait indefinitely for user decision - no timeout, no auto-reject.
            // Only manual "Stop" or "Reject" buttons resolve this.
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
        ApplySelectedConfiguredProvider();
        var conversation = SelectedConversation?.Conversation;
        var settings = Settings;
        var revision = Interlocked.Increment(ref _contextUsageRevision);

        _contextUsageCts?.Cancel();
        _contextUsageCts?.Dispose();

        if (conversation is null)
        {
            _contextUsageCts = null;
            ContextUsage = CreateEmptyContextUsage(settings);
            return;
        }

        if (_contextUsageCache.TryGetValue(conversation.Id, out var cachedUsage))
        {
            ContextUsage = cachedUsage;
        }
        else
        {
            ContextUsage = CreateEmptyContextUsage(settings);
        }

        var cts = new CancellationTokenSource();
        _contextUsageCts = cts;
        _ = UpdateContextUsageAsync(conversation, settings, revision, cts.Token);
    }

    private static ContextUsage CreateEmptyContextUsage(AppSettings settings)
    {
        return new ContextUsage
        {
            CurrentTokens = 0,
            ConversationLimit = Math.Min(settings.ModelContextLimit, 64_000),
            ModelLimit = settings.ModelContextLimit
        };
    }

    private async Task UpdateContextUsageAsync(
        Conversation conversation,
        AppSettings settings,
        int revision,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(150, cancellationToken);
            var usage = await Task.Run(() =>
            {
                var messages = conversation.Messages.ToList();
                cancellationToken.ThrowIfCancellationRequested();

                var estimatedUsage = _fastContextEstimator.Estimate(messages, settings);
                if (!settings.UseTokenizerEstimation || messages.Count == 0)
                {
                    return estimatedUsage;
                }

                cancellationToken.ThrowIfCancellationRequested();
                return _contextEstimator.Estimate(messages, settings);
            }, cancellationToken);

            if (cancellationToken.IsCancellationRequested || revision != _contextUsageRevision)
            {
                return;
            }

            await InvokeOnUiAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && revision == _contextUsageRevision)
                {
                    _contextUsageCache[conversation.Id] = usage;
                    ContextUsage = usage;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // The fast estimate is already visible; tokenizer refinement is best-effort.
        }
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
        if (_toolRegistry is null)
        {
            return;
        }

        var knownIds = _toolRegistry.All.Select(tool => tool.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Settings.EnabledToolIds = Settings.EnabledToolIds
            .Where(knownIds.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (Settings.EnabledToolIds.Count == 0)
        {
            Settings.EnabledToolIds = _toolRegistry.All
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

        foreach (var tool in _toolRegistry.All)
        {
            Settings.ToolPermissionModes.TryAdd(tool.Id, _toolRegistry.GetMetadata(tool.Id).DefaultPermissionMode);
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
        Settings.AgentMaxToolRounds = Math.Clamp(Settings.AgentMaxToolRounds, 1, 100);
        Settings.RetryMaxAttempts = Math.Clamp(Settings.RetryMaxAttempts, 0, 10);
        Settings.MaxOutputTokens = Math.Clamp(Settings.MaxOutputTokens, 256, 32768);
        Settings.ConversationContextRatio = Math.Clamp(Settings.ConversationContextRatio, 0.3, 1.0);
        Settings.AuditLogRetentionDays = Math.Clamp(Settings.AuditLogRetentionDays, 1, 365);
        if (Settings.AuditLogMaxFileSizeBytes < 1024 * 1024)
            Settings.AuditLogMaxFileSizeBytes = 5 * 1024 * 1024;
        OnPropertyChanged(nameof(AgentMaxToolRounds));
        OnPropertyChanged(nameof(RetryMaxAttempts));
        OnPropertyChanged(nameof(MaxOutputTokens));
        OnPropertyChanged(nameof(ConversationContextRatio));
        OnPropertyChanged(nameof(UseTokenizerEstimation));
        OnPropertyChanged(nameof(AuditLogMaxFileSizeMB));
        OnPropertyChanged(nameof(AuditLogRetentionDays));
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
        if (_toolRegistry is null)
        {
            return;
        }

        var enabled = Settings.EnabledToolIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ToolOptions.Clear();
        foreach (var (tool, meta) in _toolRegistry.AllWithMetadata())
        {
            var mode = Settings.ToolPermissionModes.TryGetValue(tool.Id, out var configuredMode)
                ? configuredMode
                : meta.DefaultPermissionMode;
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

    private Task RecordAuditEventAsync(AuditEventType type, string projectId, string runId, string toolName = "", string summary = "", string detail = "")
    {
        return AuditEventRecorder.RecordAsync(_auditLogRepository, type, projectId, runId, toolName, summary, detail);
    }

    private static Dictionary<string, ToolPermissionMode> MergeToolPermissionModes(
        Dictionary<string, ToolPermissionMode> global,
        Dictionary<string, string>? projectOverrides)
    {
        if (projectOverrides is null or { Count: 0 })
        {
            return global;
        }

        var merged = new Dictionary<string, ToolPermissionMode>(global, StringComparer.OrdinalIgnoreCase);
        foreach (var (toolId, modeName) in projectOverrides)
        {
            if (Enum.TryParse<ToolPermissionMode>(modeName, ignoreCase: true, out var mode))
            {
                merged[toolId] = mode;
            }
        }

        return merged;
    }

    private void LoadProjectToolPermissionOverrides()
    {
        ProjectToolPermissionOverrides.Clear();
        var project = SelectedProject?.Project;
        if (project is null) return;

        foreach (var (toolId, modeName) in project.ProjectToolPermissionModes)
        {
            var vm = new ProjectToolPermissionOverrideViewModel
            {
                ToolId = toolId,
                PermissionMode = modeName,
                PermissionModeOptions = ToolPermissionModeOptions
            };
            vm.PropertyChanged += (_, _) => SaveProjectToolPermissionOverrides();
            ProjectToolPermissionOverrides.Add(vm);
        }
    }

    private void SaveProjectToolPermissionOverrides()
    {
        var project = SelectedProject?.Project;
        if (project is null) return;

        project.ProjectToolPermissionModes = ProjectToolPermissionOverrides
            .Where(o => !string.IsNullOrWhiteSpace(o.ToolId))
            .ToDictionary(
                o => o.ToolId,
                o => o.PermissionMode,
                StringComparer.OrdinalIgnoreCase);
    }

    private void AddProjectToolOverride()
    {
        var firstTool = _toolRegistry?.All.FirstOrDefault();
        var vm = new ProjectToolPermissionOverrideViewModel
        {
            ToolId = firstTool?.Id ?? "",
            PermissionMode = nameof(ToolPermissionMode.ConfirmEachTime),
            PermissionModeOptions = ToolPermissionModeOptions
        };
        vm.PropertyChanged += (_, _) => SaveProjectToolPermissionOverrides();
        ProjectToolPermissionOverrides.Add(vm);
        SaveProjectToolPermissionOverrides();
    }

    private void RemoveProjectToolOverride(string? toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return;
        var existing = ProjectToolPermissionOverrides.FirstOrDefault(o =>
            string.Equals(o.ToolId, toolId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ProjectToolPermissionOverrides.Remove(existing);
            SaveProjectToolPermissionOverrides();
        }
    }

    private sealed record WorkspaceRunSnapshot(string Branch, int ChangeCount, bool IsTruncated);

    public sealed record ModelOptionItem(string Id, string DisplayName);

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
        OnPropertyChanged(nameof(ActiveModelSupportsTools));
    }

    private async Task AddConfiguredProviderAsync()
    {
        var template = ChatProviderCatalog.Resolve(_newProviderTemplateId);
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

        var template = ChatProviderCatalog.Resolve(_newProviderTemplateId);
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
