using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Agents.Budget;
using AIChat.Application.Llm.Resilience;
using AIChat.Application.Tools;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

// Minimal Agent loop: send enabled tool schemas to the model, execute requested
// project tools, append tool results, and ask the model to continue.
public sealed class AgentRunner
{
    private readonly IChatCompletionService _chatService;
    private readonly AgentToolCatalog _toolCatalog;
    private readonly ToolExecutionService _toolExecutionService;
    private readonly RetryPolicy _retryPolicy;

    public AgentRunner(IChatCompletionService chatService, AgentToolCatalog toolCatalog)
        : this(chatService, toolCatalog, new ToolExecutionService(toolCatalog))
    {
    }

    public AgentRunner(
        IChatCompletionService chatService,
        AgentToolCatalog toolCatalog,
        ToolExecutionService toolExecutionService,
        RetryPolicy? retryPolicy = null)
    {
        _chatService = chatService;
        _toolCatalog = toolCatalog;
        _toolExecutionService = toolExecutionService;
        _retryPolicy = retryPolicy ?? new RetryPolicy();
    }

    public async IAsyncEnumerable<AgentRunEvent> RunAsync(
        ChatRequest initialRequest,
        AppSettings settings,
        AgentRunContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enabledTools = _toolCatalog.ResolveEnabled(context.EnabledToolIds);
        var budgetManager = new AgentBudgetManager(new AgentBudget
        {
            MaxToolCalls = context.MaxToolRounds,
            PauseBeforeHighRiskMutation = false,
            ToolCheckpointInterval = 0
        });
        var sessionAllowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transcript = initialRequest.Messages
            .Select(CloneMessage)
            .ToList();
        AgentToolResult? lastToolResult = null;

        for (var round = 0; ; round++)
        {
            var request = new ChatRequest
            {
                Model = initialRequest.Model,
                Temperature = initialRequest.Temperature,
                Messages = transcript,
                Tools = enabledTools.Select(tool => tool.Definition).ToList()
            };

            var assistantContent = "";
            var assistantReasoningContent = "";
            var rawEvents = new List<string>();
            var requestedToolCalls = new List<ChatToolCall>();
            var pendingEvents = new List<AgentRunEvent>();
            var chatSucceeded = false;

            for (var retryAttempt = 0; retryAttempt <= _retryPolicy.MaxRetries && !chatSucceeded; retryAttempt++)
            {
                yield return new AgentRunEvent { Type = AgentRunEventType.ModelRequestStarted };
                assistantContent = "";
                assistantReasoningContent = "";
                rawEvents.Clear();
                requestedToolCalls.Clear();
                pendingEvents.Clear();
                var hadTransientError = false;
                AgentRunEvent? terminalEvent = null;

                try
                {
                    await foreach (var delta in _chatService.SendAsync(request, settings, cancellationToken))
                    {
                        if (!string.IsNullOrWhiteSpace(delta.RawJson))
                        {
                            rawEvents.Add(delta.RawJson);
                            pendingEvents.Add(new AgentRunEvent
                            {
                                Type = AgentRunEventType.RawProviderEvent,
                                RawJson = delta.RawJson
                            });
                        }

                        if (delta.HttpStatusCode is > 0 && RetryPolicy.IsTransientHttpError(delta.HttpStatusCode.Value))
                        {
                            hadTransientError = true;
                            break;
                        }

                        if (!string.IsNullOrEmpty(delta.ReasoningContent))
                        {
                            assistantReasoningContent += delta.ReasoningContent;
                        }

                        if (!string.IsNullOrEmpty(delta.Content))
                        {
                            assistantContent += delta.Content;
                        }

                        if (delta.ToolCalls.Count > 0)
                        {
                            requestedToolCalls.AddRange(delta.ToolCalls);
                        }
                    }

                    chatSucceeded = !hadTransientError;
                }
                catch (HttpRequestException)
                {
                    hadTransientError = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    terminalEvent = new AgentRunEvent
                    {
                        Type = AgentRunEventType.Cancelled,
                        Content = "Agent 运行已取消。"
                    };
                }
                catch (Exception ex)
                {
                    terminalEvent = new AgentRunEvent
                    {
                        Type = AgentRunEventType.Error,
                        Content = $"LLM 请求失败：{ex.Message}"
                    };
                }

                if (terminalEvent is not null)
                {
                    yield return terminalEvent;
                    yield return new AgentRunEvent { Type = AgentRunEventType.Completed };
                    yield break;
                }

                if (hadTransientError && retryAttempt < _retryPolicy.MaxRetries)
                {
                    await Task.Delay(_retryPolicy.GetDelay(retryAttempt), cancellationToken);
                }
                else if (hadTransientError)
                {
                    // Exhausted retries — yield error and stop
                    yield return new AgentRunEvent
                    {
                        Type = AgentRunEventType.Error,
                        Content = "LLM 请求失败（网络或服务端错误），已重试多次仍无法连接。"
                    };
                    yield return new AgentRunEvent { Type = AgentRunEventType.Completed };
                    yield break;
                }
            }

            // Yield buffered raw events
            foreach (var pending in pendingEvents)
            {
                yield return pending;
            }

            if (requestedToolCalls.Count == 0)
            {
                if (!string.IsNullOrEmpty(assistantContent))
                {
                    yield return new AgentRunEvent
                    {
                        Type = AgentRunEventType.ContentDelta,
                        Content = assistantContent
                    };
                }

                yield return new AgentRunEvent { Type = AgentRunEventType.Completed };
                yield break;
            }

            var assistantToolMessage = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = assistantContent,
                ReasoningContent = assistantReasoningContent,
                ToolCalls = requestedToolCalls
                    .Select(call => new ChatToolCall
                    {
                        Id = call.Id,
                        Index = call.Index,
                        Name = call.Name,
                        ArgumentsJson = call.ArgumentsJson
                    })
                    .ToList()
            };
            transcript.Add(assistantToolMessage);

            foreach (var toolCall in requestedToolCalls)
            {
                var toolRisk = _toolCatalog.Find(toolCall.Name)?.Risk ?? AgentToolRisk.Shell;
                var budgetDecision = budgetManager.ConsumeToolCall(
                    toolCall.Name,
                    isHighRiskMutation: toolRisk != AgentToolRisk.ReadOnly,
                    allowCheckpointPause: false);
                if (budgetDecision.IsHardLimit)
                {
                    yield return new AgentRunEvent
                    {
                        Type = AgentRunEventType.BudgetExceeded,
                        Content = budgetDecision.Reason
                    };
                    yield return new AgentRunEvent { Type = AgentRunEventType.Completed };
                    yield break;
                }

                yield return new AgentRunEvent
                {
                    Type = AgentRunEventType.ToolCall,
                    ToolCall = toolCall
                };

                AgentToolResult? result = null;
                await foreach (var toolEvent in _toolExecutionService.ExecuteAsync(
                                   new ToolExecutionRequest
                                   {
                                       ToolCall = toolCall,
                                       ProjectPath = context.ProjectPath,
                                       ToolPermissionModes = context.ToolPermissionModes,
                                       SessionAllowedToolIds = sessionAllowedTools,
                                       InputArtifacts = context.InputArtifacts,
                                       RequestToolApprovalAsync = context.RequestToolApprovalAsync
                                   },
                                   cancellationToken))
                {
                    switch (toolEvent.Type)
                    {
                        case ToolExecutionEventType.ApprovalRequired:
                            yield return new AgentRunEvent
                            {
                                Type = AgentRunEventType.ToolApprovalRequired,
                                ToolCall = toolEvent.ToolCall,
                                ToolPreview = toolEvent.Preview
                            };
                            break;
                        case ToolExecutionEventType.ApprovalRejected:
                            yield return new AgentRunEvent
                            {
                                Type = AgentRunEventType.ToolApprovalRejected,
                                ToolCall = toolEvent.ToolCall,
                                ToolPreview = toolEvent.Preview
                            };
                            break;
                        case ToolExecutionEventType.SessionAllowed:
                            if (!string.IsNullOrWhiteSpace(toolEvent.SessionAllowedToolId))
                            {
                                sessionAllowedTools.Add(toolEvent.SessionAllowedToolId);
                                yield return new AgentRunEvent
                                {
                                    Type = AgentRunEventType.ToolSessionAllowed,
                                    ToolCall = toolEvent.ToolCall,
                                    SessionAllowedToolId = toolEvent.SessionAllowedToolId
                                };
                            }
                            break;
                        case ToolExecutionEventType.Result:
                            result = toolEvent.Result;
                            yield return new AgentRunEvent
                            {
                                Type = AgentRunEventType.ToolResult,
                                ToolCall = toolEvent.ToolCall,
                                ToolPreview = toolEvent.Preview,
                                ToolResult = toolEvent.Result
                            };
                            break;
                    }
                }

                if (result is null)
                {
                    result = new AgentToolResult
                    {
                        ToolName = toolCall.Name,
                        Content = "工具没有返回结果。",
                        IsError = true
                    };
                    yield return new AgentRunEvent
                    {
                        Type = AgentRunEventType.ToolResult,
                        ToolCall = toolCall,
                        ToolResult = result
                    };
                }

                lastToolResult = result;

                if (result.Status == ToolExecutionStatus.Cancelled)
                {
                    yield return new AgentRunEvent
                    {
                        Type = AgentRunEventType.Cancelled,
                        Content = result.Content
                    };
                    yield return new AgentRunEvent { Type = AgentRunEventType.Completed };
                    yield break;
                }

                transcript.Add(new ChatMessage
                {
                    Role = ChatRole.Tool,
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    Content = result.ContentForModel,
                    CreatedAt = DateTimeOffset.Now
                });
            }
        }
    }

    private static ChatMessage CloneMessage(ChatMessage message)
    {
        return new ChatMessage
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            Role = message.Role,
            Content = message.Content,
            ContentParts = message.ContentParts.Select(CloneContentPart).ToList(),
            ReasoningContent = message.ReasoningContent,
            ToolCallId = message.ToolCallId,
            ToolName = message.ToolName,
            ToolCalls = message.ToolCalls
                .Select(call => new ChatToolCall
                {
                    Id = call.Id,
                    Index = call.Index,
                    Name = call.Name,
                    ArgumentsJson = call.ArgumentsJson
                })
                .ToList(),
            IsError = message.IsError,
            CreatedAt = message.CreatedAt
        };
    }

    private static ChatContentPart CloneContentPart(ChatContentPart part)
    {
        return new ChatContentPart
        {
            Type = part.Type,
            Text = part.Text,
            MediaType = part.MediaType,
            DataBase64 = part.DataBase64,
            SourcePath = part.SourcePath
        };
    }
}
