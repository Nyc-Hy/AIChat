using System.IO;
using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents;
using AIChat.Application.Audit;
using AIChat.Application.Configuration;
using AIChat.Application.Projects;
using AIChat.Application.Tools;
using AIChat.Domain.Audit;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
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

    public bool AgentAdaptiveStrategiesEnabled
    {
        get => Settings.AgentAdaptiveStrategiesEnabled;
        set
        {
            if (Settings.AgentAdaptiveStrategiesEnabled == value)
            {
                return;
            }

            Settings.AgentAdaptiveStrategiesEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool AgentAdaptiveBudgetAndExplorerEnabled
    {
        get => Settings.AgentAdaptiveBudgetAndExplorerEnabled;
        set
        {
            if (Settings.AgentAdaptiveBudgetAndExplorerEnabled == value)
            {
                return;
            }

            Settings.AgentAdaptiveBudgetAndExplorerEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool AgentAdaptiveRecoveryEnabled
    {
        get => Settings.AgentAdaptiveRecoveryEnabled;
        set
        {
            if (Settings.AgentAdaptiveRecoveryEnabled == value)
            {
                return;
            }

            Settings.AgentAdaptiveRecoveryEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool AgentAdaptiveAutoVerifyEnabled
    {
        get => Settings.AgentAdaptiveAutoVerifyEnabled;
        set
        {
            if (Settings.AgentAdaptiveAutoVerifyEnabled == value)
            {
                return;
            }

            Settings.AgentAdaptiveAutoVerifyEnabled = value;
            OnPropertyChanged();
        }
    }

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
        OnPropertyChanged(nameof(AgentAdaptiveStrategiesEnabled));
        OnPropertyChanged(nameof(AgentAdaptiveBudgetAndExplorerEnabled));
        OnPropertyChanged(nameof(AgentAdaptiveRecoveryEnabled));
        OnPropertyChanged(nameof(AgentAdaptiveAutoVerifyEnabled));
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
