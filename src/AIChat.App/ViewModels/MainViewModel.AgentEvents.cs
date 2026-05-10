using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.App.ViewModels;

public sealed partial class MainViewModel
{
    private sealed class AgentUiEventState
    {
        public bool HasReceivedContent { get; set; }
        public bool HasShownToolProgress { get; set; }
        public bool HasUsedTools { get; set; }

        public Dictionary<string, ToolTraceViewModel> ToolTraceByCallId { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, AgentStepViewModel> StepByToolCallId { get; } = new(StringComparer.Ordinal);
    }

    private async Task ApplyAgentHarnessUiEventAsync(
        AgentHarnessEvent agentEvent,
        ChatMessageViewModel assistantViewModel,
        AgentUiEventState state,
        CancellationToken cancellationToken)
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

                    StatusText = "正在处理...";
                    AgentStatusPhase = "正在处理";
                    OnPropertyChanged(nameof(HasAgentStatus));
                });
                break;

            case AgentHarnessEventType.PhaseChanged:
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    assistantViewModel.SyncAgentPhase();
                    AgentStatusPhase = agentEvent.Run is null
                        ? ""
                        : agentEvent.Run.Phase switch
                        {
                            "verifying" => "正在验证",
                            "repairing" => "正在修复",
                            "waiting_for_user" => "等待用户",
                            "completed" => "已完成",
                            "cancelled" => "已停止",
                            "failed" => "失败",
                            _ => "正在处理"
                        };
                    OnPropertyChanged(nameof(HasAgentStatus));
                });
                break;

            case AgentHarnessEventType.StepAdded:
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (agentEvent.Step is not null)
                    {
                        _ = assistantViewModel.AddAgentStep(agentEvent.Step);
                    }

                    assistantViewModel.SyncAgentPlan();
                });
                break;

            case AgentHarnessEventType.SubAgentStarted:
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = "正在处理...";
                    AgentStatusPhase = "正在处理";
                    AgentStatusTool = "";
                    assistantViewModel.SyncSubAgentRuns();
                    OnPropertyChanged(nameof(HasAgentStatus));
                });
                break;

            case AgentHarnessEventType.SubAgentCompleted:
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    assistantViewModel.SyncSubAgentRuns();
                    assistantViewModel.SyncAgentArtifacts();
                    StatusText = "正在处理...";
                    AgentStatusTool = "";
                    OnPropertyChanged(nameof(HasAgentStatus));
                });
                break;

            case AgentHarnessEventType.ContentDelta:
                if (!state.HasReceivedContent)
                {
                    state.HasReceivedContent = true;
                    state.HasShownToolProgress = false;
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        assistantViewModel.Content = "";
                        StatusText = "正在处理...";
                        AgentStatusPhase = "正在回复";
                        AgentStatusTool = "";
                        OnPropertyChanged(nameof(HasAgentStatus));
                    });
                }

                await AppendAssistantContentAsync(assistantViewModel, agentEvent.Content, cancellationToken);
                break;

            case AgentHarnessEventType.ToolCall:
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    StatusText = "正在处理...";
                    AgentStatusPhase = "正在处理";
                    AgentStatusTool = "";
                    OnPropertyChanged(nameof(HasAgentStatus));
                    state.HasUsedTools = true;
                    if (!state.HasReceivedContent && !state.HasShownToolProgress)
                    {
                        state.HasShownToolProgress = true;
                        assistantViewModel.Content = "正在查看项目文件并分析结果...";
                    }

                    if (agentEvent.ToolCall is not null)
                    {
                        state.ToolTraceByCallId[agentEvent.ToolCall.Id] = assistantViewModel.AddToolTrace(agentEvent.ToolCall);
                        var stepViewModel = agentEvent.Step is null
                            ? null
                            : assistantViewModel.AddAgentStep(agentEvent.Step);
                        if (stepViewModel is not null)
                        {
                            state.StepByToolCallId[agentEvent.ToolCall.Id] = stepViewModel;
                        }
                    }
                });
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
                break;

            case AgentHarnessEventType.ToolResult:
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (agentEvent.ToolCall is not null &&
                        agentEvent.ToolResult is not null &&
                        state.ToolTraceByCallId.TryGetValue(agentEvent.ToolCall.Id, out var trace))
                    {
                        trace.Complete(agentEvent.ToolResult.Content, agentEvent.ToolResult.IsError);
                    }

                    if (agentEvent.ToolCall is not null &&
                        agentEvent.ToolResult is not null &&
                        state.StepByToolCallId.TryGetValue(agentEvent.ToolCall.Id, out var step))
                    {
                        step.Refresh();
                    }

                    assistantViewModel.SyncAgentFileChanges();
                    assistantViewModel.SyncAgentVerifications();
                    assistantViewModel.SyncAgentArtifacts();
                    assistantViewModel.SyncAgentPlan();
                });
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
                        assistantViewModel.SyncAgentArtifacts();
                        assistantViewModel.SyncSubAgentRuns();
                        assistantViewModel.AgentRun?.Complete(agentEvent.Run.Status);
                        RebuildAgentRunHistoryIfOpen();
                    }

                    AgentStatusPhase = agentEvent.Run?.Status switch
                    {
                        AgentRunStatus.Completed => "已完成",
                        AgentRunStatus.BudgetExceeded => "已暂停",
                        _ => "已结束"
                    };
                    AgentStatusTool = "";
                    OnPropertyChanged(nameof(HasAgentStatus));
                });
                break;
        }
    }
}
