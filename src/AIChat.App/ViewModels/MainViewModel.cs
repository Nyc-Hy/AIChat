using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using AIChat.App.Controls;
using AIChat.App.Services;
using AIChat.Application.Artifacts;
using AIChat.Domain.Audit;
using AIChat.Domain.Artifacts;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Context;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.Application.Agents;
using AIChat.Application.Audit;
using AIChat.Application.Configuration;
using AIChat.Application.Context;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Projects;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using AIChat.Domain.Context;

namespace AIChat.App.ViewModels;

// Main application state machine. This ViewModel coordinates UI state,
// persistence, context estimation, and model calls without depending on WPF
// controls directly.
public sealed partial class MainViewModel : ObservableObject
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
    private readonly AgentRequestFactory _agentRequestFactory;
    private readonly WorkspaceChangeService _workspaceChangeService;
    private readonly AgentRunAuditService? _auditService;
    private readonly InputArtifactService _inputArtifactService = new();
    private readonly InputArtifactFileStore _inputArtifactFileStore = new();
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
    private bool _isInputArtifactPreviewOpen;
    private ProjectViewModel? _projectPendingRemoval;
    private InputArtifactViewModel? _selectedInputArtifactPreview;
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
        AgentRunAuditService? auditService = null)
    {
        _repository = repository;
        _chatService = chatService;
        _contextEstimator = contextEstimator;
        _agentRequestFactory = new AgentRequestFactory(contextBuilder);
        _workspaceChangeService = workspaceChangeService;
        _auditService = auditService;
        // Commands are the bridge from XAML buttons/menu items to ViewModel methods.
        NewChatCommand = new RelayCommand(_ => NewChat(), _ => SelectedProject is not null && !IsSending);
        SendCommand = new RelayCommand(async _ => await SendAsync(), _ => CanSend);
        AttachInputArtifactCommand = new RelayCommand(async _ => await AttachInputArtifactAsync(), _ => SelectedProject is not null && SelectedConversation is not null && !IsSending);
        RemoveInputArtifactCommand = new RelayCommand(async parameter => await RemoveInputArtifactAsync((InputArtifactViewModel)parameter!), parameter => parameter is InputArtifactViewModel && !IsSending);
        OpenInputArtifactPreviewCommand = new RelayCommand(parameter => OpenInputArtifactPreview(parameter as InputArtifactViewModel), parameter => parameter is InputArtifactViewModel { IsImagePreview: true });
        CloseInputArtifactPreviewCommand = new RelayCommand(_ => CloseInputArtifactPreview());
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
        AddProjectVerificationCommandCommand = new RelayCommand(_ => AddProjectVerificationCommand(), _ => SelectedProject is not null);
        RemoveProjectVerificationCommandCommand = new RelayCommand(param => RemoveProjectVerificationCommand(param as ProjectVerificationCommandViewModel), param => param is ProjectVerificationCommandViewModel);
        InferProjectVerificationCommandsCommand = new RelayCommand(_ => InferProjectVerificationCommands(), _ => SelectedProject is not null && Directory.Exists(SelectedProject.Path));
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
    public ObservableCollection<InputArtifactViewModel> CurrentInputArtifacts { get; } = [];
    public ObservableCollection<ProjectVerificationCommandViewModel> ProjectVerificationCommands { get; } = [];
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
    public RelayCommand AttachInputArtifactCommand { get; }
    public RelayCommand RemoveInputArtifactCommand { get; }
    public RelayCommand OpenInputArtifactPreviewCommand { get; }
    public RelayCommand CloseInputArtifactPreviewCommand { get; }
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
    public RelayCommand AddProjectVerificationCommandCommand { get; }
    public RelayCommand RemoveProjectVerificationCommandCommand { get; }
    public RelayCommand InferProjectVerificationCommandsCommand { get; }
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
                OnPropertyChanged(nameof(AutoVerifyAgentRuns));
                OnPropertyChanged(nameof(MaxAutoFixRounds));
                OnPropertyChanged(nameof(RetryMaxAttempts));
                OnPropertyChanged(nameof(MaxOutputTokens));
                OnPropertyChanged(nameof(ConversationContextRatio));
                OnPropertyChanged(nameof(UseTokenizerEstimation));
                OnPropertyChanged(nameof(AuditLogMaxFileSizeMB));
                OnPropertyChanged(nameof(AuditLogRetentionDays));
                OnPropertyChanged(nameof(ActiveModelSupportsVision));
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
                AttachInputArtifactCommand.RaiseCanExecuteChanged();
                AddProjectVerificationCommandCommand.RaiseCanExecuteChanged();
                InferProjectVerificationCommandsCommand.RaiseCanExecuteChanged();
                RebuildCurrentInputArtifacts();
                LoadProjectToolPermissionOverrides();
                LoadProjectVerificationCommands();
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
                AttachInputArtifactCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CurrentInputArtifactSummary));
                RebuildCurrentInputArtifacts();
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
    public bool HasCurrentInputArtifacts => CurrentInputArtifacts.Count > 0;
    public string CurrentInputArtifactSummary
    {
        get
        {
            var summary = BuildCurrentInputArtifactDeliverySummary(ActiveModelSupportsVision);
            return summary.TotalCount == 0 ? "" : summary.SummaryText;
        }
    }
    public string ModelName => SelectedConfiguredProvider is null
        ? "未配置模型"
        : $"{SelectedConfiguredProvider.Name} · {SelectedConfiguredProvider.SelectedModelId}";
    public IReadOnlyList<ConfiguredLlmProvider> ConfiguredProviders => Settings.ConfiguredProviders;
    public ConfiguredLlmProvider? SelectedConfiguredProvider => ProviderSettingsService.GetSelectedProvider(Settings);
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
            var label = string.IsNullOrWhiteSpace(model.CapabilityLabel)
                ? "标准聊天能力"
                : model.CapabilityLabel;
            return configured.SupportsVisionOverride && model.Capabilities.SupportsVision == false
                ? label + " · vision override"
                : label;
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
    public bool ActiveModelSupportsVision
    {
        get
        {
            var configured = SelectedConfiguredProvider;
            if (configured is null) return false;
            var model = ChatProviderCatalog.ResolveModel(configured.TemplateId, configured.SelectedModelId);
            return model.Capabilities?.SupportsVision == true || configured.SupportsVisionOverride;
        }
    }
    public bool SelectedConfiguredProviderSupportsVisionOverride
    {
        get => SelectedConfiguredProvider?.SupportsVisionOverride == true;
        set
        {
            var configured = SelectedConfiguredProvider;
            if (configured is null || configured.SupportsVisionOverride == value)
            {
                return;
            }

            configured.SupportsVisionOverride = value;
            ApplySelectedConfiguredProvider();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveModelSupportsVision));
            OnPropertyChanged(nameof(ActiveModelCapabilitySummary));
            RebuildCurrentInputArtifacts();
            _ = PersistSettingsQuietlyAsync();
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

            ProviderSettingsService.SelectProviderTemplate(Settings, value);
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
            ProviderSettingsService.ApplySelectedProvider(Settings);
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

            if (!ProviderSettingsService.SelectActiveModel(Settings, value))
            {
                return;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Settings));
            OnPropertyChanged(nameof(ModelName));
            OnPropertyChanged(nameof(SelectedConfiguredProvider));
            OnPropertyChanged(nameof(SelectedConfiguredProviderSupportsVisionOverride));
            RebuildModelParameterOptions();
            RebuildCurrentInputArtifacts();
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

    public bool IsRemoveProjectConfirmationOpen
    {
        get => _isRemoveProjectConfirmationOpen;
        private set => SetProperty(ref _isRemoveProjectConfirmationOpen, value);
    }

    public bool IsInputArtifactPreviewOpen
    {
        get => _isInputArtifactPreviewOpen;
        private set => SetProperty(ref _isInputArtifactPreviewOpen, value);
    }

    public InputArtifactViewModel? SelectedInputArtifactPreview
    {
        get => _selectedInputArtifactPreview;
        private set => SetProperty(ref _selectedInputArtifactPreview, value);
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
                AttachInputArtifactCommand.RaiseCanExecuteChanged();
                RemoveInputArtifactCommand.RaiseCanExecuteChanged();
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
            var normalized = AdvancedSettingsService.NormalizeAgentMaxToolRounds(value);
            if (Settings.AgentMaxToolRounds == normalized)
            {
                return;
            }

            Settings.AgentMaxToolRounds = normalized;
            OnPropertyChanged();
        }
    }

    public bool AutoVerifyAgentRuns
    {
        get => Settings.AutoVerifyAgentRuns;
        set
        {
            if (Settings.AutoVerifyAgentRuns == value)
            {
                return;
            }

            Settings.AutoVerifyAgentRuns = value;
            OnPropertyChanged();
        }
    }

    public int MaxAutoFixRounds
    {
        get => Settings.MaxAutoFixRounds;
        set
        {
            var normalized = AdvancedSettingsService.NormalizeMaxAutoFixRounds(value);
            if (Settings.MaxAutoFixRounds == normalized)
            {
                return;
            }

            Settings.MaxAutoFixRounds = normalized;
            OnPropertyChanged();
        }
    }

    public bool HasProjectVerificationCommands => ProjectVerificationCommands.Count > 0;
    public string ProjectVerificationCommandSummary => HasProjectVerificationCommands
        ? $"{ProjectVerificationCommands.Count} 个项目验证命令"
        : "当前项目还没有验证命令";

    public int RetryMaxAttempts
    {
        get => Settings.RetryMaxAttempts;
        set
        {
            var normalized = AdvancedSettingsService.NormalizeRetryMaxAttempts(value);
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
            var normalized = AdvancedSettingsService.NormalizeMaxOutputTokens(value);
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
            var normalized = AdvancedSettingsService.NormalizeConversationContextRatio(value);
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
            var bytes = AdvancedSettingsService.NormalizeAuditLogMaxFileSizeMegabytes(value) * 1024 * 1024;
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
            var normalized = AdvancedSettingsService.NormalizeAuditLogRetentionDays(value);
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
        SaveProjectVerificationCommands();
        await _repository.SaveSettingsAsync(Settings);
        await SaveProjectsAsync();
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
            EnsureDefaultVerificationCommands(SelectedProject.Project);
            _ = _repository.SaveProjectsAsync(Projects.Select(p => p.Project).ToList());
            OnPropertyChanged(nameof(SelectedProject));
            LoadProjectVerificationCommands();
            InferProjectVerificationCommandsCommand.RaiseCanExecuteChanged();
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
        EnsureDefaultVerificationCommands(workspace);

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
        _inputArtifactFileStore.DeleteStoredFiles(project.Project.InputArtifacts);
        _inputArtifactFileStore.DeleteProjectStore(project.Project.Id);
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

    private async Task AttachInputArtifactAsync()
    {
        if (SelectedProject is null || SelectedConversation is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "选择输入附件",
            CheckFileExists = true,
            Multiselect = true,
            Filter = "支持的输入|*.txt;*.md;*.json;*.xml;*.yaml;*.yml;*.csv;*.tsv;*.log;*.cs;*.xaml;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.pdf;*.doc;*.docx;*.xlsx;*.xls|所有文件|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var added = 0;
        var optimized = 0;
        foreach (var fileName in dialog.FileNames)
        {
            try
            {
                var result = await AttachInputArtifactFileAsync(fileName);
                if (result.Added)
                {
                    added++;
                    if (result.Optimized)
                    {
                        optimized++;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText = $"附件读取失败：{Path.GetFileName(fileName)} - {ex.Message}";
            }
        }

        if (added > 0)
        {
            var prunedArtifacts = _inputArtifactService.PruneRemoved(SelectedProject.Project.InputArtifacts);
            _inputArtifactFileStore.DeleteStoredFiles(prunedArtifacts);
            SelectedProject.Project.UpdatedAt = DateTimeOffset.Now;
            await SaveProjectsAsync();
            var optimizedText = optimized == 0 ? "" : $"，优化 {optimized} 张图片";
            StatusText = prunedArtifacts.Count == 0
                ? $"已加入 {added} 个输入附件{optimizedText}"
                : $"已加入 {added} 个输入附件{optimizedText}，清理 {prunedArtifacts.Count} 个旧附件";
            OnPropertyChanged(nameof(CurrentInputArtifactSummary));
            RebuildCurrentInputArtifacts();
            UpdateContextUsage();
        }
    }

    public async Task AttachClipboardImageAsync(byte[] imageBytes)
    {
        if (SelectedProject is null || SelectedConversation is null || imageBytes.Length == 0)
        {
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"AIChat-clipboard-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(tempPath, imageBytes);
            var displayName = $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss}.png";
            var result = await AttachInputArtifactFileAsync(tempPath, displayName, "clipboard");
            if (!result.Added)
            {
                return;
            }

            var prunedArtifacts = _inputArtifactService.PruneRemoved(SelectedProject.Project.InputArtifacts);
            _inputArtifactFileStore.DeleteStoredFiles(prunedArtifacts);
            SelectedProject.Project.UpdatedAt = DateTimeOffset.Now;
            await SaveProjectsAsync();
            StatusText = result.Optimized
                ? "已从剪贴板加入截图附件并优化"
                : "已从剪贴板加入截图附件";
            OnPropertyChanged(nameof(CurrentInputArtifactSummary));
            RebuildCurrentInputArtifacts();
            UpdateContextUsage();
        }
        catch (Exception ex)
        {
            StatusText = $"剪贴板图片读取失败：{ex.Message}";
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private async Task<(bool Added, bool Optimized)> AttachInputArtifactFileAsync(
        string fileName,
        string? displayFileName = null,
        string? sourcePathOverride = null)
    {
        if (SelectedProject is null || SelectedConversation is null)
        {
            return (false, false);
        }

        var fileInfo = new FileInfo(fileName);
        if (!fileInfo.Exists)
        {
            return (false, false);
        }

        var mimeType = GuessMimeType(fileInfo.Extension);
        var preparedImage = InputImageAttachmentOptimizer.Prepare(fileInfo, mimeType);
        var originalDisplayName = string.IsNullOrWhiteSpace(displayFileName)
            ? fileInfo.Name
            : displayFileName.Trim();
        var attachmentFileName = preparedImage.WasOptimized
            ? Path.GetFileNameWithoutExtension(originalDisplayName) + ".jpg"
            : originalDisplayName;
        var attachmentMimeType = preparedImage.MimeType;
        var attachmentSizeBytes = preparedImage.SizeBytes;
        var contentText = ShouldReadText(fileInfo.Extension, mimeType)
            ? await ReadTextPreviewAsync(fileInfo.FullName, 200_000)
            : "";
        var fileBytes = string.IsNullOrWhiteSpace(contentText) && ShouldReadBinaryArtifact(fileInfo.Extension, mimeType, fileInfo.Length)
            ? await File.ReadAllBytesAsync(fileInfo.FullName)
            : [];
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourcePath"] = string.IsNullOrWhiteSpace(sourcePathOverride) ? fileInfo.FullName : sourcePathOverride,
            ["sizeBytes"] = attachmentSizeBytes.ToString()
        };
        if (preparedImage.PixelWidth > 0 && preparedImage.PixelHeight > 0)
        {
            metadata["imageWidth"] = preparedImage.PixelWidth.ToString();
            metadata["imageHeight"] = preparedImage.PixelHeight.ToString();
        }

        if (preparedImage.WasOptimized)
        {
            metadata["optimized"] = "true";
            metadata["originalFileName"] = originalDisplayName;
            metadata["originalSizeBytes"] = preparedImage.OriginalSizeBytes.ToString();
        }

        var artifact = _inputArtifactService.Create(new InputArtifactCreateRequest
        {
            ProjectId = SelectedProject.Project.Id,
            ConversationId = SelectedConversation.Conversation.Id,
            FileName = attachmentFileName,
            MimeType = attachmentMimeType,
            ContentText = contentText,
            FileBytes = fileBytes,
            Metadata = metadata
        });

        if (preparedImage.WasOptimized)
        {
            await _inputArtifactFileStore.StoreBytesAsync(
                artifact,
                preparedImage.OptimizedBytes,
                preparedImage.OptimizedExtension);
        }
        else
        {
            await _inputArtifactFileStore.StoreAsync(artifact, fileInfo.FullName);
        }

        SelectedProject.Project.InputArtifacts.Add(artifact);
        return (true, preparedImage.WasOptimized);
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Clipboard image temp cleanup should not hide attachment failures.
        }
    }

    private async Task RemoveInputArtifactAsync(InputArtifactViewModel artifactViewModel)
    {
        if (SelectedProject is null)
        {
            return;
        }

        var removed = SelectedProject.Project.InputArtifacts.Remove(artifactViewModel.Artifact);
        if (!removed)
        {
            var match = SelectedProject.Project.InputArtifacts.FirstOrDefault(artifact =>
                string.Equals(artifact.Id, artifactViewModel.Id, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                removed = SelectedProject.Project.InputArtifacts.Remove(match);
            }
        }

        if (!removed)
        {
            return;
        }

        _inputArtifactFileStore.DeleteStoredFile(artifactViewModel.Artifact);
        if (SelectedInputArtifactPreview?.Id == artifactViewModel.Id)
        {
            CloseInputArtifactPreview();
        }

        SelectedProject.Project.UpdatedAt = DateTimeOffset.Now;
        await SaveProjectsAsync();
        StatusText = $"已移除输入附件：{artifactViewModel.FileName}";
        RebuildCurrentInputArtifacts();
        UpdateContextUsage();
    }

    private void OpenInputArtifactPreview(InputArtifactViewModel? artifact)
    {
        if (artifact?.IsImagePreview != true)
        {
            return;
        }

        SelectedInputArtifactPreview = artifact;
        IsInputArtifactPreviewOpen = true;
    }

    private void CloseInputArtifactPreview()
    {
        IsInputArtifactPreviewOpen = false;
        SelectedInputArtifactPreview = null;
    }

    private void RebuildCurrentInputArtifacts()
    {
        CurrentInputArtifacts.Clear();
        if (SelectedProject is null || SelectedConversation is null)
        {
            RaiseInputArtifactProperties();
            return;
        }

        var artifacts = GetCurrentConversationInputArtifacts()
            .Take(8)
            .ToList();
        var visionDecisions = InputArtifactVisionPolicy.Evaluate(artifacts, ActiveModelSupportsVision)
            .ToDictionary(decision => decision.Artifact.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in artifacts)
        {
            CurrentInputArtifacts.Add(visionDecisions.TryGetValue(artifact.Id, out var decision)
                ? new InputArtifactViewModel(artifact, decision)
                : new InputArtifactViewModel(artifact, ActiveModelSupportsVision));
        }

        RaiseInputArtifactProperties();
    }

    private void RaiseInputArtifactProperties()
    {
        OnPropertyChanged(nameof(CurrentInputArtifactSummary));
        OnPropertyChanged(nameof(HasCurrentInputArtifacts));
        RemoveInputArtifactCommand.RaiseCanExecuteChanged();
    }

    private InputArtifactDeliverySummary BuildCurrentInputArtifactDeliverySummary(bool modelSupportsVision)
    {
        if (SelectedProject is null || SelectedConversation is null)
        {
            return InputArtifactDeliverySummary.Empty;
        }

        var artifacts = GetCurrentConversationInputArtifacts().ToList();
        if (artifacts.Count == 0)
        {
            return InputArtifactDeliverySummary.Empty;
        }

        var decisions = InputArtifactVisionPolicy.Evaluate(artifacts, modelSupportsVision);
        var imageCount = decisions.Count(decision => decision.IsImage);
        var sendableImageCount = decisions.Count(decision => decision.CanSend);
        var referencedImageCount = imageCount - sendableImageCount;
        var nonImageCount = artifacts.Count - imageCount;

        var parts = new List<string> { $"{artifacts.Count} 个输入附件" };
        if (sendableImageCount > 0)
        {
            parts.Add($"{sendableImageCount} 张图片将发送");
        }

        if (referencedImageCount > 0)
        {
            parts.Add($"{referencedImageCount} 张图片仅引用");
        }

        if (nonImageCount > 0 && imageCount > 0)
        {
            parts.Add($"{nonImageCount} 个文本/文件引用");
        }

        if (imageCount == 0)
        {
            parts.Add("已加入上下文");
        }

        return new InputArtifactDeliverySummary(
            artifacts.Count,
            imageCount,
            sendableImageCount,
            referencedImageCount,
            string.Join(" · ", parts));
    }

    private IReadOnlyList<InputArtifact> GetCurrentConversationInputArtifacts()
    {
        if (SelectedProject is null || SelectedConversation is null)
        {
            return [];
        }

        var conversationId = SelectedConversation.Conversation.Id;
        return SelectedProject.Project.InputArtifacts
            .Where(artifact => string.IsNullOrWhiteSpace(artifact.ConversationId) ||
                               string.Equals(artifact.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(artifact => artifact.CreatedAt)
            .ToList();
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

    private static bool ShouldReadText(string extension, string mimeType)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
               ext is "txt" or "md" or "json" or "xml" or "yaml" or "yml" or "csv" or "tsv" or "log" or
                   "cs" or "xaml" or "csproj" or "sln" or "props" or "targets";
    }

    private static bool ShouldReadBinaryArtifact(string extension, string mimeType, long sizeBytes)
    {
        const long maxExtractBytes = 8 * 1024 * 1024;
        if (sizeBytes <= 0 || sizeBytes > maxExtractBytes)
        {
            return false;
        }

        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext is "pdf" or "docx" or "xlsx" ||
               mimeType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
               mimeType.Contains("officedocument", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadTextPreviewAsync(string path, int maxChars)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192);
        var buffer = new char[maxChars + 1];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
        return new string(buffer, 0, Math.Min(read, maxChars));
    }

    private static string GuessMimeType(string extension)
    {
        return extension.TrimStart('.').ToLowerInvariant() switch
        {
            "txt" or "log" => "text/plain",
            "md" => "text/markdown",
            "json" => "application/json",
            "xml" or "xaml" or "csproj" or "props" or "targets" => "application/xml",
            "yaml" or "yml" => "application/yaml",
            "csv" => "text/csv",
            "tsv" => "text/tab-separated-values",
            "cs" => "text/x-csharp",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "bmp" => "image/bmp",
            "pdf" => "application/pdf",
            "doc" => "application/msword",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xls" => "application/vnd.ms-excel",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
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
        var removedArtifacts = _inputArtifactService.RemoveForConversation(
            project.Project.InputArtifacts,
            conversation.Conversation.Id);
        _inputArtifactFileStore.DeleteStoredFiles(removedArtifacts);
        if (SelectedConversation == conversation)
        {
            SelectConversation(project.Conversations.First());
        }

        RebuildCurrentInputArtifacts();
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
        ProviderSettingsService.Normalize(Settings, AgentDefaultTemperature);
        OnPropertyChanged(nameof(SelectedProviderId));
        OnPropertyChanged(nameof(SelectedActiveModelId));
        OnPropertyChanged(nameof(ActiveModelOptions));
        OnPropertyChanged(nameof(ConfiguredProviders));
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(SelectedConfiguredProviderId));
        OnPropertyChanged(nameof(SelectedConfiguredProvider));
        OnPropertyChanged(nameof(SelectedActiveModelId));
        OnPropertyChanged(nameof(ActiveModelOptions));
    }

    private void NormalizeToolSettings()
    {
        if (_toolRegistry is null) return;
        ToolSettingsService.Normalize(Settings, _toolRegistry);
    }

    private void NormalizeHarnessSettings()
    {
        AdvancedSettingsService.Normalize(Settings);
        OnPropertyChanged(nameof(AgentMaxToolRounds));
        OnPropertyChanged(nameof(AutoVerifyAgentRuns));
        OnPropertyChanged(nameof(MaxAutoFixRounds));
        OnPropertyChanged(nameof(RetryMaxAttempts));
        OnPropertyChanged(nameof(MaxOutputTokens));
        OnPropertyChanged(nameof(ConversationContextRatio));
        OnPropertyChanged(nameof(UseTokenizerEstimation));
        OnPropertyChanged(nameof(AuditLogMaxFileSizeMB));
        OnPropertyChanged(nameof(AuditLogRetentionDays));
    }

    private void NormalizeModelParameters()
    {
        ProviderSettingsService.NormalizeModelParameters(Settings);
    }

    private void RebuildToolOptions()
    {
        if (_toolRegistry is null)
        {
            return;
        }

        ToolOptions.Clear();
        foreach (var tool in ToolSettingsService.CreateToolOptions(Settings, _toolRegistry))
        {
            ToolOptions.Add(new ToolOptionViewModel
            {
                Id = tool.Id,
                Name = tool.Name,
                Description = tool.Description,
                RiskLabel = tool.Risk switch
                {
                    AgentToolRisk.ReadOnly => "只读",
                    AgentToolRisk.Write => "写入",
                    AgentToolRisk.Shell => "Shell",
                    _ => "工具"
                },
                PermissionModeOptions = ToolPermissionModeOptions,
                IsEnabled = tool.IsEnabled,
                PermissionMode = tool.PermissionMode.ToString()
            });
        }

        OnPropertyChanged(nameof(ToolOptions));
    }

    private void SyncToolOptionsToSettings()
    {
        ToolSettingsService.SyncToolOptions(
            Settings,
            ToolOptions.Select(tool => (tool.Id, tool.IsEnabled, tool.PermissionMode)));
    }

    private Task RecordAuditEventAsync(AuditEventType type, string projectId, string runId, string toolName = "", string summary = "", string detail = "")
    {
        return _auditService?.RecordAsync(type, projectId, runId, toolName, summary, detail) ?? Task.CompletedTask;
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

        project.ProjectToolPermissionModes = ToolSettingsService.CreateProjectOverrides(
            ProjectToolPermissionOverrides.Select(o => (o.ToolId, o.PermissionMode)));
    }

    private void LoadProjectVerificationCommands()
    {
        foreach (var command in ProjectVerificationCommands)
        {
            command.PropertyChanged -= ProjectVerificationCommand_PropertyChanged;
        }

        ProjectVerificationCommands.Clear();
        var project = SelectedProject?.Project;
        if (project is null)
        {
            RaiseProjectVerificationCommandChanges();
            return;
        }

        if (EnsureDefaultVerificationCommands(project))
        {
            _ = SaveProjectsAsync();
        }
        foreach (var command in project.VerificationCommands)
        {
            var vm = new ProjectVerificationCommandViewModel(command);
            vm.PropertyChanged += ProjectVerificationCommand_PropertyChanged;
            ProjectVerificationCommands.Add(vm);
        }

        RaiseProjectVerificationCommandChanges();
    }

    private void ProjectVerificationCommand_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        SaveProjectVerificationCommands();
        RaiseProjectVerificationCommandChanges();
    }

    private void SaveProjectVerificationCommands()
    {
        var project = SelectedProject?.Project;
        if (project is null)
        {
            return;
        }

        project.VerificationCommands = ProjectVerificationCommands
            .Select(command => command.Command)
            .Where(command => !string.IsNullOrWhiteSpace(command.Name) ||
                              !string.IsNullOrWhiteSpace(command.Command) ||
                              !string.IsNullOrWhiteSpace(command.WorkingDirectory))
            .ToList();
        project.UpdatedAt = DateTimeOffset.Now;
    }

    private void AddProjectVerificationCommand()
    {
        if (SelectedProject is null)
        {
            return;
        }

        var command = new ProjectVerificationCommand
        {
            Name = "验证",
            Command = "dotnet test",
            WorkingDirectory = FindDefaultVerificationTarget(SelectedProject.Path),
            TimeoutSeconds = 180
        };
        var vm = new ProjectVerificationCommandViewModel(command);
        vm.PropertyChanged += ProjectVerificationCommand_PropertyChanged;
        ProjectVerificationCommands.Add(vm);
        SaveProjectVerificationCommands();
        RaiseProjectVerificationCommandChanges();
    }

    private void RemoveProjectVerificationCommand(ProjectVerificationCommandViewModel? command)
    {
        if (command is null)
        {
            return;
        }

        command.PropertyChanged -= ProjectVerificationCommand_PropertyChanged;
        ProjectVerificationCommands.Remove(command);
        SaveProjectVerificationCommands();
        RaiseProjectVerificationCommandChanges();
    }

    private void InferProjectVerificationCommands()
    {
        var project = SelectedProject?.Project;
        if (project is null)
        {
            return;
        }

        var suggestions = new ProjectInitializer().SuggestVerificationCommands(project.Path);
        if (suggestions.Count == 0)
        {
            StatusText = "没有从当前项目识别到可用验证命令";
            return;
        }

        project.VerificationCommands = suggestions.ToList();
        LoadProjectVerificationCommands();
        StatusText = $"已推断 {suggestions.Count} 个验证命令";
    }

    private static bool EnsureDefaultVerificationCommands(ProjectWorkspace project)
    {
        if (project.VerificationCommands.Count > 0 ||
            string.IsNullOrWhiteSpace(project.Path) ||
            !Directory.Exists(project.Path))
        {
            return false;
        }

        project.VerificationCommands = new ProjectInitializer()
            .SuggestVerificationCommands(project.Path)
            .ToList();
        return project.VerificationCommands.Count > 0;
    }

    private static string FindDefaultVerificationTarget(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            return "";
        }

        var target = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(projectPath, "*.slnx", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(target) ? "" : Path.GetFileName(target);
    }

    private void RaiseProjectVerificationCommandChanges()
    {
        OnPropertyChanged(nameof(ProjectVerificationCommands));
        OnPropertyChanged(nameof(HasProjectVerificationCommands));
        OnPropertyChanged(nameof(ProjectVerificationCommandSummary));
        AddProjectVerificationCommandCommand.RaiseCanExecuteChanged();
        RemoveProjectVerificationCommandCommand.RaiseCanExecuteChanged();
        InferProjectVerificationCommandsCommand.RaiseCanExecuteChanged();
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
        var values = ProviderSettingsService.NormalizeModelParameterValues(configured.TemplateId, model.Id, configured.ModelParameters);
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
            configured.ModelParameters = ProviderSettingsService.NormalizeModelParameterValues(
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
        OnPropertyChanged(nameof(ActiveModelSupportsVision));
        OnPropertyChanged(nameof(SelectedConfiguredProviderSupportsVisionOverride));
    }

    private async Task AddConfiguredProviderAsync()
    {
        var result = ProviderSettingsService.AddConfiguredProvider(
            Settings,
            _newProviderTemplateId,
            NewProviderApiKey);
        NewProviderApiKey = "";
        await _repository.SaveSettingsAsync(Settings);
        RaiseConfiguredProviderChanges();
        StatusText = result.AlreadyExisted
            ? "该模型提供商已存在，已切换到该配置"
            : $"{result.Provider.Name} 已添加";
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
        if (!ProviderSettingsService.RemoveSelectedProvider(Settings))
        {
            return;
        }

        await _repository.SaveSettingsAsync(Settings);
        RaiseConfiguredProviderChanges();
        StatusText = "模型提供商已移除";
    }

    private AppSettings? CreateEffectiveSettings()
    {
        return ProviderSettingsService.CreateEffectiveSettings(Settings, AgentDefaultTemperature);
    }

    private void ApplySelectedConfiguredProvider()
    {
        ProviderSettingsService.ApplySelectedProvider(Settings);
    }

    private void RaiseConfiguredProviderChanges()
    {
        OnPropertyChanged(nameof(ConfiguredProviders));
        OnPropertyChanged(nameof(SelectedConfiguredProvider));
        OnPropertyChanged(nameof(SelectedConfiguredProviderId));
        OnPropertyChanged(nameof(ActiveModelOptions));
        OnPropertyChanged(nameof(SelectedActiveModelId));
        OnPropertyChanged(nameof(SelectedConfiguredProviderSupportsVisionOverride));
        RebuildModelParameterOptions();
        RebuildCurrentInputArtifacts();
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

}

internal sealed record InputArtifactDeliverySummary(
    int TotalCount,
    int ImageCount,
    int SendableImageCount,
    int ReferencedImageCount,
    string SummaryText)
{
    public static InputArtifactDeliverySummary Empty { get; } = new(0, 0, 0, 0, "");
}
