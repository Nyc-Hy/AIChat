using System.IO;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    private void InitializeCommands()
    {
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
        AcceptSelectedAgentRunCommand = new RelayCommand(async _ => await AcceptSelectedAgentRunAsync(), _ => SelectedAgentRunDetails is not null && !IsSending);
        RequestChangesSelectedAgentRunCommand = new RelayCommand(async _ => await RequestChangesSelectedAgentRunAsync(), _ => SelectedAgentRunDetails is not null && !IsSending);
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
}
