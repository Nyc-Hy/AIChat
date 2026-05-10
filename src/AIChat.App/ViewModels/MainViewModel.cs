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


}
