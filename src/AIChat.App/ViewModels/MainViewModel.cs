using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using AIChat.App.Services;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Context;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.Application.Agents;
using AIChat.Application.Agents.Benchmark;
using AIChat.Application.Audit;
using AIChat.Application.Context;
using AIChat.Application.Memory;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Prompting;
using AIChat.Application.Projects;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Application.Artifacts;
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
    private readonly AgentRunMemoryExtractor _memoryExtractor = new();
    private readonly MemoryService _memoryService = new();
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
    private string _selectedBenchmarkTaskId = AgentBenchmarkSuite.DefaultTasks[0].Id;
    private int _agentRunHistoryTotalCount;
    private string _projectMemorySearchText = "";
    private string _projectMemoryFilterId = "all";
    private bool _projectMemoryClearArmed;
    private string _selectedCallRequestJson = "请选择左侧调用记录。";
    private string _selectedCallResponseJson = "请选择左侧调用记录。";
    private bool _showSelectedCallRawEvents;
    private WorkspaceChangeViewModel? _selectedWorkspaceChange;
    private string _agentRunHistoryFilterId = "all";
    private string _workspaceBranch = "";
    private string _workspaceStatusText = "尚未刷新";
    private string _workspaceDiffText = "选择一个变更文件查看 diff。";
    private IReadOnlyList<DiffLineViewModel> _workspaceDiffLines = [];
    private ProjectLoadSnapshot _projectLoadSnapshot = new(
        "健康：未选择项目",
        "画像：无",
        "活动：无",
        "建议：先添加或选择一个项目。");
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
        InitializeCommands();
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
    public ObservableCollection<ProjectMemoryViewModel> ProjectMemories { get; } = [];
    public IReadOnlyList<AgentBenchmarkTaskOptionViewModel> BenchmarkTaskOptions { get; } =
        AgentBenchmarkSuite.DefaultTasks.Select(task => new AgentBenchmarkTaskOptionViewModel(task)).ToList();
    public IReadOnlyList<SelectionOptionViewModel> AgentRunHistoryFilterOptions => AgentRunHistoryFilter.Options;
    public IReadOnlyList<SelectionOptionViewModel> ProjectMemoryFilterOptions { get; } =
    [
        new() { Id = "all", Name = "全部" },
        new() { Id = "pending", Name = "待确认" },
        new() { Id = "project", Name = "项目" },
        new() { Id = "task", Name = "任务" },
        new() { Id = "tool", Name = "工具" },
        new() { Id = "user", Name = "用户" }
    ];
    public IReadOnlyList<SelectionOptionViewModel> ToolPermissionModeOptions { get; } =
    [
        new() { Id = nameof(ToolPermissionMode.AutoReadOnly), Name = "只读自动" },
        new() { Id = nameof(ToolPermissionMode.ConfirmEachTime), Name = "每次确认" },
        new() { Id = nameof(ToolPermissionMode.AllowForSession), Name = "本会话允许" },
        new() { Id = nameof(ToolPermissionMode.Disabled), Name = "关闭" }
    ];
    public IReadOnlyList<LlmProviderInfo> ProviderOptions { get; } = ChatProviderCatalog.All;
    public RelayCommand NewChatCommand { get; private set; } = null!;
    public RelayCommand SendCommand { get; private set; } = null!;
    public RelayCommand AttachInputArtifactCommand { get; private set; } = null!;
    public RelayCommand RemoveInputArtifactCommand { get; private set; } = null!;
    public RelayCommand OpenInputArtifactPreviewCommand { get; private set; } = null!;
    public RelayCommand CloseInputArtifactPreviewCommand { get; private set; } = null!;
    public RelayCommand SelectProjectCommand { get; private set; } = null!;
    public RelayCommand SelectConversationCommand { get; private set; } = null!;
    public RelayCommand LoadEarlierMessagesCommand { get; private set; } = null!;
    public RelayCommand OpenSettingsCommand { get; private set; } = null!;
    public RelayCommand CloseSettingsCommand { get; private set; } = null!;
    public RelayCommand SaveSettingsCommand { get; private set; } = null!;
    public RelayCommand StopCommand { get; private set; } = null!;
    public RelayCommand CopyMessageCommand { get; private set; } = null!;
    public RelayCommand CopyConversationTitleCommand { get; private set; } = null!;
    public RelayCommand RenameConversationCommand { get; private set; } = null!;
    public RelayCommand DeleteConversationCommand { get; private set; } = null!;
    public RelayCommand OpenCallDetailsCommand { get; private set; } = null!;
    public RelayCommand CloseCallDetailsCommand { get; private set; } = null!;
    public RelayCommand OpenAgentRunHistoryCommand { get; private set; } = null!;
    public RelayCommand RunBenchmarkCommand { get; private set; } = null!;
    public RelayCommand CloseAgentRunHistoryCommand { get; private set; } = null!;
    public RelayCommand SelectAgentRunHistoryItemCommand { get; private set; } = null!;
    public RelayCommand RetryAgentRunCommand { get; private set; } = null!;
    public RelayCommand OpenAgentRunDetailsCommand { get; private set; } = null!;
    public RelayCommand CloseAgentRunDetailsCommand { get; private set; } = null!;
    public RelayCommand AddConfiguredProviderCommand { get; private set; } = null!;
    public RelayCommand RemoveConfiguredProviderCommand { get; private set; } = null!;
    public RelayCommand ToggleNewProviderApiKeyVisibilityCommand { get; private set; } = null!;
    public RelayCommand TestProviderConnectionCommand { get; private set; } = null!;
    public RelayCommand RefreshWorkspaceChangesCommand { get; private set; } = null!;
    public RelayCommand RestoreWorkspaceFileCommand { get; private set; } = null!;
    public RelayCommand CommitWorkspaceFileCommand { get; private set; } = null!;
    public RelayCommand CommitAllWorkspaceChangesCommand { get; private set; } = null!;
    public RelayCommand OpenWorkspaceFileCommand { get; private set; } = null!;
    public RelayCommand CopyWorkspacePathCommand { get; private set; } = null!;
    public RelayCommand CopyWorkspaceDiffCommand { get; private set; } = null!;
    public RelayCommand StageSelectedWorkspaceChangesCommand { get; private set; } = null!;
    public RelayCommand UnstageSelectedWorkspaceChangesCommand { get; private set; } = null!;
    public RelayCommand SelectAllWorkspaceChangesCommand { get; private set; } = null!;
    public RelayCommand ClearWorkspaceSelectionCommand { get; private set; } = null!;
    public RelayCommand CommitAgentRunChangesCommand { get; private set; } = null!;
    public RelayCommand RestoreAgentRunChangesCommand { get; private set; } = null!;
    public RelayCommand CopyAgentRunChangeSummaryCommand { get; private set; } = null!;
    public RelayCommand CopySelectedAgentRunSummaryCommand { get; private set; } = null!;
    public RelayCommand CopySelectedAgentRunReviewPacketCommand { get; private set; } = null!;
    public RelayCommand AcceptSelectedAgentRunCommand { get; private set; } = null!;
    public RelayCommand RequestChangesSelectedAgentRunCommand { get; private set; } = null!;
    public RelayCommand RetrySelectedAgentRunCommand { get; private set; } = null!;
    public RelayCommand ContinueAgentRunCommand { get; private set; } = null!;
    public RelayCommand ContinueSelectedAgentRunCommand { get; private set; } = null!;
    public RelayCommand OpenAgentFileChangeCommand { get; private set; } = null!;
    public RelayCommand CopyAgentFilePathCommand { get; private set; } = null!;
    public RelayCommand CopyAgentFileDiffCommand { get; private set; } = null!;
    public RelayCommand CopyTraceCommand { get; private set; } = null!;
    public RelayCommand ApproveToolCommand { get; private set; } = null!;
    public RelayCommand ApproveToolForSessionCommand { get; private set; } = null!;
    public RelayCommand RejectToolCommand { get; private set; } = null!;
    public RelayCommand AddProjectToolOverrideCommand { get; private set; } = null!;
    public RelayCommand RemoveProjectToolOverrideCommand { get; private set; } = null!;
    public RelayCommand AddProjectVerificationCommandCommand { get; private set; } = null!;
    public RelayCommand RemoveProjectVerificationCommandCommand { get; private set; } = null!;
    public RelayCommand InferProjectVerificationCommandsCommand { get; private set; } = null!;
    public RelayCommand ProjectNextActionCommand { get; private set; } = null!;
    public RelayCommand FixProjectHealthCommand { get; private set; } = null!;
    public RelayCommand RefreshProjectSnapshotCommand { get; private set; } = null!;
    public RelayCommand GenerateProjectAgentsCommand { get; private set; } = null!;
    public RelayCommand RemoveProjectMemoryCommand { get; private set; } = null!;
    public RelayCommand AcceptProjectMemoryCommand { get; private set; } = null!;
    public RelayCommand DeduplicateProjectMemoriesCommand { get; private set; } = null!;
    public RelayCommand ClearProjectMemoriesCommand { get; private set; } = null!;
    public RelayCommand AddProjectCommand { get; private set; } = null!;
    public RelayCommand RemoveProjectCommand { get; private set; } = null!;
    public RelayCommand ConfirmRemoveProjectCommand { get; private set; } = null!;
    public RelayCommand CancelRemoveProjectCommand { get; private set; } = null!;

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

    public string SelectedBenchmarkTaskId
    {
        get => _selectedBenchmarkTaskId;
        set => SetProperty(ref _selectedBenchmarkTaskId, string.IsNullOrWhiteSpace(value)
            ? AgentBenchmarkSuite.DefaultTasks[0].Id
            : value);
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
                RunBenchmarkCommand.RaiseCanExecuteChanged();
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

    private bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(DraftMessage) && SelectedProject is not null;

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

}
