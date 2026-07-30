using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Abstractions.Persistence;
using AIChat.App.Avalonia.Composition;
using AIChat.Application.Agents;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Context;
using AIChat.Application.Prompting;
using AIChat.Application.Projects;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIChat.App.Avalonia.ViewModels;

// Owns the inner agent loop: building the AgentHarness, streaming events,
// updating the activity feed, and persisting the conversation when the run
// finishes. Extracted from MainWindowViewModel in PR-13 so the host VM only
// owns the user-facing SendTaskCommand (validation + entry point) and
// cross-VM coordination.
//
// The host's IsRunning / StatusMessage / DraftPrompt stay the source of
// truth for the XAML bindings; the runner writes through the small set of
// Action/Func callbacks passed in. All other dependencies (activity feed,
// insights, sidebar, conversation list, repository, chat service, tool
// registry, approval service) are held directly because the runner is the
// sole writer of those during a run.
public sealed partial class AgentRunnerViewModel : ObservableObject
{
    private readonly IChatCompletionService _chatService;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly IApprovalService _approval;
    private readonly IAppRepository _repository;
    private readonly ActivityFeedViewModel _activityFeed;
    private readonly SessionInsightsViewModel _insights;
    private readonly ProjectSidebarViewModel _sidebar;
    private readonly ConversationListViewModel _conversationList;

    // Host-owned state setters. The host's IsRunning is what the XAML
    // binds to (and the CanExecute on SendTaskCommand depends on it), so
    // the runner writes through these callbacks instead of duplicating
    // the observable here.
    private readonly Action<bool> _setIsRunning;
    private readonly Action<string> _setStatusMessage;
    private readonly Action _clearDraftPrompt;
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<bool> _getNoWriteMode;

    public AgentRunnerViewModel(
        IChatCompletionService chatService,
        AgentToolRegistry toolRegistry,
        IApprovalService approval,
        IAppRepository repository,
        ActivityFeedViewModel activityFeed,
        SessionInsightsViewModel insights,
        ProjectSidebarViewModel sidebar,
        ConversationListViewModel conversationList,
        Action<bool> setIsRunning,
        Action<string> setStatusMessage,
        Action clearDraftPrompt,
        Func<AppSettings> getSettings,
        Func<bool> getNoWriteMode)
    {
        _chatService = chatService;
        _toolRegistry = toolRegistry;
        _approval = approval;
        _repository = repository;
        _activityFeed = activityFeed;
        _insights = insights;
        _sidebar = sidebar;
        _conversationList = conversationList;
        _setIsRunning = setIsRunning;
        _setStatusMessage = setStatusMessage;
        _clearDraftPrompt = clearDraftPrompt;
        _getSettings = getSettings;
        _getNoWriteMode = getNoWriteMode;
    }

    // Entry point. The host has already validated the prompt, settings,
    // and project; this method assumes all preconditions hold.
    public async Task RunAsync(string prompt, AppSettings effectiveSettings)
    {
        _setIsRunning(true);
        _clearDraftPrompt();
        var userItem = new ActivityItemViewModel("你", prompt, "已发送");
        var assistantItem = new ActivityItemViewModel(
            "AIChat",
            _getNoWriteMode() ? "正在以只读模式启动..." : "正在启动任务...",
            "运行中");
        _activityFeed.Add(userItem);
        _activityFeed.Add(assistantItem);
        _setStatusMessage("AIChat 正在读取上下文...");

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
            var settings = _getSettings();
            var noWrite = _getNoWriteMode();
            var runtimeSettings = noWrite
                ? RuntimeSettingsBuilder.ReadOnly(settings, _toolRegistry)
                : RuntimeSettingsBuilder.Gui(settings, _toolRegistry);
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
            _setStatusMessage("完成。");
        }
        catch (Exception ex)
        {
            assistantItem.Status = "失败";
            assistantItem.Detail = $"请求失败：{ex.Message}";
            _setStatusMessage("请求失败。");
        }
        finally
        {
            _setIsRunning(false);
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
                        _setStatusMessage(agentEvent.PhaseTransition.Summary);
                    });
                }

                break;
            case AgentHarnessEventType.ToolCall:
                if (!string.IsNullOrWhiteSpace(agentEvent.ToolCall?.Name))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _activityFeed.Add(
                            "正在读取",
                            FriendlyToolSummary(agentEvent.ToolCall.Name),
                            "工具");
                        _insights.UpdateMetrics(agentEvent.Run, assistantMessage.Content, _sidebar.CurrentProject?.VerificationCommands.Count ?? 0);
                    });
                }

                break;
            case AgentHarnessEventType.ToolApprovalRejected:
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _activityFeed.Add(
                        "已跳过操作",
                        agentEvent.ToolPreview?.Summary ?? "此操作需要确认后才能执行。",
                        "已阻止");
                });
                break;
            case AgentHarnessEventType.ToolResult:
                if (agentEvent.ToolResult?.IsError == true)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _activityFeed.Add(
                            "工具问题",
                            agentEvent.ToolResult.Content,
                            "需查看");
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
                        _setStatusMessage("正在接收回复...");
                        _insights.UpdateMetrics(agentEvent.Run, assistantMessage.Content, _sidebar.CurrentProject?.VerificationCommands.Count ?? 0);
                    });
                }

                break;
            case AgentHarnessEventType.RunCompleted:
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _setStatusMessage(agentEvent.Run?.CompletionReason is { Length: > 0 } reason ? reason : "运行完成。");
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
