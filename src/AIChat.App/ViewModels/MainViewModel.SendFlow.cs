using AIChat.Application.Agents;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Prompting;
using AIChat.Abstractions.Configuration;
using AIChat.Domain.Audit;
using AIChat.Domain.Chat;
using System.Text;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
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

        var inputArtifactDelivery = BuildCurrentInputArtifactDeliverySummary(effectiveSettings.ModelSupportsVision);
        var text = DraftMessage.Trim();
        DraftMessage = "";
        var continuedFromRunId = _pendingContinuedFromRunId;
        var retriedFromRunId = _pendingRetriedFromRunId;
        _pendingContinuedFromRunId = "";
        _pendingRetriedFromRunId = "";
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
        var agentUiState = new AgentUiEventState();
        var reasoningContentBuilder = new System.Text.StringBuilder();
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
            RequestJson = "请求正在构建..."
        };
        SelectedConversation.AddCallDetail(callDetail);

        if (!_agentRunQueue.TryStart(assistantMessage.Id))
        {
            StatusText = "已有任务运行中，请等待完成后再试";
            return;
        }

        IsSending = true;
        IsStopping = false;
        StatusText = inputArtifactDelivery.TotalCount > 0
            ? $"正在连接模型... {inputArtifactDelivery.SummaryText}"
            : "正在连接模型...";
        _sendCts = new CancellationTokenSource();
        var workspaceSnapshot = await CaptureWorkspaceSnapshotAsync(_sendCts.Token);
        var rawResponseEvents = new List<string>();
        var requestBuild = _agentRequestFactory.Build(new AgentRequestBuildRequest
        {
            Conversation = SelectedConversation.Conversation,
            AssistantMessageId = assistantMessage.Id,
            EffectiveSettings = effectiveSettings,
            RuntimeSettings = Settings,
            ProjectName = SelectedProject?.Name ?? "AIChat",
            ProjectPath = SelectedProject?.Path ?? "",
            WorkspaceBranch = workspaceSnapshot.Branch,
            WorkspaceChangeCount = workspaceSnapshot.ChangeCount,
            ProjectLoadSnapshot = string.Join(Environment.NewLine, [
                CurrentProjectHealthText,
                CurrentProjectProfileText,
                CurrentProjectActivityText,
                CurrentProjectRecommendationText
            ]),
            PinnedContextItems = SelectedProject?.Project.PinnedContext ?? [],
            InputArtifacts = SelectedProject?.Project.InputArtifacts ?? [],
            MemoryEntries = SelectedProject?.Project.Memories ?? [],
            ProjectToolPermissionModes = SelectedProject?.Project.ProjectToolPermissionModes,
            VerificationCommands = SelectedProject?.Project.VerificationCommands ?? [],
            RequestToolApprovalAsync = RequestToolApprovalAsync
        });
        var request = requestBuild.ChatRequest;
        callDetail.RequestJson = SerializeJson(AgentRequestFactory.CreateSnapshot(
            request,
            effectiveSettings,
            Settings,
            ToolOptions
                .Where(tool => tool.IsEnabled)
                .Select(tool => tool.Id)));
        SelectedConversation.RefreshCallDetail(callDetail);

        try
        {
            // Give WPF one dispatcher turn so the placeholder message can render
            // before the network call begins.
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { });

            await ExecuteSendRequestAsync(
                request,
                requestBuild,
                effectiveSettings,
                workspaceSnapshot,
                userMessage.Id,
                assistantMessage.Id,
                text,
                continuedFromRunId,
                retriedFromRunId,
                assistantViewModel,
                agentUiState,
                reasoningContentBuilder,
                rawResponseEvents,
                _sendCts.Token);

            // Store reasoning content for DeepSeek thinking mode replay.
            if (reasoningContentBuilder.Length > 0)
            {
                assistantMessage.ReasoningContent = reasoningContentBuilder.ToString();
            }

            if (!agentUiState.HasReceivedContent)
            {
                assistantViewModel.Content = agentUiState.HasUsedTools
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
            if (!agentUiState.HasReceivedContent)
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

    private async Task ExecuteSendRequestAsync(
        ChatRequest request,
        AgentRequestBuildResult requestBuild,
        AppSettings effectiveSettings,
        WorkspaceRunSnapshot workspaceSnapshot,
        string userMessageId,
        string assistantMessageId,
        string goal,
        string continuedFromRunId,
        string retriedFromRunId,
        ChatMessageViewModel assistantViewModel,
        AgentUiEventState agentUiState,
        StringBuilder reasoningContentBuilder,
        List<string> rawResponseEvents,
        CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            var modelInfo = ChatProviderCatalog.ResolveModel(
                effectiveSettings.ActiveConfiguredProviderId,
                effectiveSettings.Model);
            var supportsTools = modelInfo?.Capabilities?.SupportsTools == true;

            if (_agentHarness is null || !supportsTools)
            {
                await ExecutePlainChatAsync(
                    request,
                    effectiveSettings,
                    assistantViewModel,
                    agentUiState,
                    reasoningContentBuilder,
                    rawResponseEvents,
                    cancellationToken);

                if (_agentHarness is not null && !supportsTools)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        StatusText = "当前模型不支持工具调用，已回退到普通聊天模式";
                    });
                }

                return;
            }

            await ExecuteAgentRunAsync(
                request,
                requestBuild,
                effectiveSettings,
                workspaceSnapshot,
                userMessageId,
                assistantMessageId,
                goal,
                continuedFromRunId,
                retriedFromRunId,
                assistantViewModel,
                agentUiState,
                rawResponseEvents,
                cancellationToken);
        }, cancellationToken);
    }

    private async Task ExecutePlainChatAsync(
        ChatRequest request,
        AppSettings effectiveSettings,
        ChatMessageViewModel assistantViewModel,
        AgentUiEventState agentUiState,
        StringBuilder reasoningContentBuilder,
        List<string> rawResponseEvents,
        CancellationToken cancellationToken)
    {
        await foreach (var delta in _chatService.SendAsync(request, effectiveSettings, cancellationToken))
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
                if (!agentUiState.HasReceivedContent)
                {
                    // Replace the placeholder text as soon as the first real token arrives.
                    agentUiState.HasReceivedContent = true;
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        assistantViewModel.Content = "";
                        StatusText = "模型正在回复...";
                    });
                }

                await AppendAssistantContentAsync(assistantViewModel, delta.Content, cancellationToken);
            }
        }
    }

    private async Task ExecuteAgentRunAsync(
        ChatRequest request,
        AgentRequestBuildResult requestBuild,
        AppSettings effectiveSettings,
        WorkspaceRunSnapshot workspaceSnapshot,
        string userMessageId,
        string assistantMessageId,
        string goal,
        string continuedFromRunId,
        string retriedFromRunId,
        ChatMessageViewModel assistantViewModel,
        AgentUiEventState agentUiState,
        List<string> rawResponseEvents,
        CancellationToken cancellationToken)
    {
        if (_agentHarness is null)
        {
            return;
        }

        await foreach (var agentEvent in _agentHarness.RunAsync(
                           new AgentHarnessRunRequest
                           {
                               Conversation = SelectedConversation!.Conversation,
                               UserMessageId = userMessageId,
                               AssistantMessageId = assistantMessageId,
                               Goal = goal,
                               ChatRequest = request,
                               Settings = effectiveSettings,
                               ContextPack = requestBuild.ContextPack,
                               WorkspaceBranch = workspaceSnapshot.Branch,
                               WorkspaceChangeCountAtStart = workspaceSnapshot.ChangeCount,
                               WorkspaceChangesWereTruncated = workspaceSnapshot.IsTruncated,
                               ContinuedFromRunId = continuedFromRunId,
                               RetriedFromRunId = retriedFromRunId,
                               Context = requestBuild.AgentContext
                           },
                           cancellationToken))
        {
            switch (agentEvent.Type)
            {
                case AgentHarnessEventType.RunStarted:
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
                    await RecordAuditEventAsync(AuditEventType.AgentRunStarted,
                        SelectedProject?.Project.Id ?? "", agentEvent.Run?.Id ?? "",
                        summary: goal);
                    break;
                case AgentHarnessEventType.PhaseChanged:
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
                    break;
                case AgentHarnessEventType.StepAdded:
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
                    break;
                case AgentHarnessEventType.SubAgentStarted:
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
                    await RecordAuditEventAsync(AuditEventType.SubAgentStarted,
                        SelectedProject?.Project.Id ?? "", agentEvent.Run?.Id ?? "",
                        toolName: agentEvent.SubAgentRun?.TemplateId ?? "",
                        summary: $"Sub-agent started: {agentEvent.SubAgentRun?.TemplateId}",
                        detail: agentEvent.SubAgentRun?.Task ?? "");
                    break;
                case AgentHarnessEventType.SubAgentCompleted:
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
                    await RecordAuditEventAsync(
                        string.Equals(agentEvent.SubAgentRun?.Status, "Completed", StringComparison.OrdinalIgnoreCase)
                            ? AuditEventType.SubAgentCompleted
                            : AuditEventType.SubAgentFailed,
                        SelectedProject?.Project.Id ?? "", agentEvent.Run?.Id ?? "",
                        toolName: agentEvent.SubAgentRun?.TemplateId ?? "",
                        summary: $"Sub-agent {agentEvent.SubAgentRun?.Status}: {agentEvent.SubAgentRun?.TemplateId}",
                        detail: agentEvent.SubAgentRun?.Summary ?? "");
                    break;
                case AgentHarnessEventType.RawProviderEvent:
                    if (!string.IsNullOrWhiteSpace(agentEvent.RawJson))
                    {
                        rawResponseEvents.Add(agentEvent.RawJson);
                    }
                    break;
                case AgentHarnessEventType.ContentDelta:
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
                    break;
                case AgentHarnessEventType.ToolCall:
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
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
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
                    break;
                case AgentHarnessEventType.ToolApprovalRejected:
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
                    await RecordAuditEventAsync(AuditEventType.ToolCallRejected,
                        SelectedProject?.Project.Id ?? "", agentEvent.Run?.Id ?? "",
                        toolName: agentEvent.ToolCall?.Name ?? "",
                        summary: $"Rejected: {agentEvent.ToolCall?.Name}");
                    break;
                case AgentHarnessEventType.ToolResult:
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
                    rawResponseEvents.Add(SerializeJson(new
                    {
                        type = "tool_result",
                        tool = agentEvent.ToolResult?.ToolName,
                        isError = agentEvent.ToolResult?.IsError,
                        content = agentEvent.ToolResult?.Content
                    }));
                    break;
                case AgentHarnessEventType.RunCompleted:
                    await ApplyAgentHarnessUiEventAsync(agentEvent, assistantViewModel, agentUiState, cancellationToken);
                    var runStatus = agentEvent.Run?.Status;
                    var auditType = runStatus switch
                    {
                        AgentRunStatus.Completed => AuditEventType.AgentRunCompleted,
                        AgentRunStatus.BudgetExceeded => AuditEventType.AgentRunCancelled,
                        AgentRunStatus.Failed => AuditEventType.AgentRunFailed,
                        AgentRunStatus.Cancelled => AuditEventType.AgentRunCancelled,
                        _ => AuditEventType.AgentRunCompleted
                    };
                    await RecordAuditEventAsync(auditType,
                        SelectedProject?.Project.Id ?? "", agentEvent.Run?.Id ?? "",
                        summary: $"Run {runStatus}");
                    break;
            }
        }
    }

}
