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
//
// The loop is intentionally tiny — it alternates between two helpers
// (SendChatRoundAsync + ExecuteToolCallsAsync) until the model emits no
// more tool calls or one of them signals stop. The previous shape had all
// three rounds inline in one 250+ line method with deep nesting; the
// split keeps the retry / transient-error handling in one place and
// the tool execution / budget handling in another.
public sealed class AgentRunner
{
    private readonly IChatCompletionService _chatService;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly RetryPolicy _retryPolicy;

    // Trailing state of the last round. Per-instance fields — safe
    // because AgentHarness is the only caller and serializes per
    // conversation (one RunAsync at a time per runner instance).
    // The two helpers can't return a value through an
    // IAsyncEnumerable<T> at yield break, so they stash their
    // final state here and RunAsync reads it after the foreach
    // exhausts.
    private ChatRoundState _lastChatState = new("", "", [], ShouldStop: true, Usage: null);
    private ToolRoundState _lastToolState = new(Stop: false, Cancelled: false);
    private AgentRunEvent? _terminalChatEvent;

    public AgentRunner(
        IChatCompletionService chatService,
        AgentToolRegistry toolRegistry,
        RetryPolicy? retryPolicy = null)
    {
        _chatService = chatService;
        _toolRegistry = toolRegistry;
        _retryPolicy = retryPolicy ?? new RetryPolicy();
    }

    public async IAsyncEnumerable<AgentRunEvent> RunAsync(
        ChatRequest initialRequest,
        AppSettings settings,
        AgentRunContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enabledTools = _toolRegistry.ResolveEnabled(context.EnabledToolIds);
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

        var chatRequest = new ChatRequest
        {
            Model = initialRequest.Model,
            Temperature = initialRequest.Temperature,
            Messages = transcript,
            Tools = enabledTools.Select(tool => tool.Definition).ToList()
        };

        while (true)
        {
            await foreach (var evt in SendChatRoundAsync(chatRequest, settings, cancellationToken))
            {
                yield return evt;
                if (evt is { Type: AgentRunEventType.Cancelled or AgentRunEventType.Error })
                {
                    yield return new AgentRunEvent { Type = AgentRunEventType.Completed };
                    yield break;
                }
            }

            var chat = _lastChatState;
            if (chat.ShouldStop)
            {
                if (!string.IsNullOrEmpty(chat.Content))
                {
                    yield return new AgentRunEvent
                    {
                        Type = AgentRunEventType.ContentDelta,
                        Content = chat.Content
                    };
                }
                yield return new AgentRunEvent { Type = AgentRunEventType.Completed };
                yield break;
            }

            transcript.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = chat.Content,
                ReasoningContent = chat.ReasoningContent,
                ToolCalls = chat.ToolCalls
                    .Select(call => new ChatToolCall
                    {
                        Id = call.Id,
                        Index = call.Index,
                        Name = call.Name,
                        ArgumentsJson = call.ArgumentsJson
                    })
                    .ToList()
            });

            await foreach (var evt in ExecuteToolCallsAsync(
                               chat.ToolCalls, transcript, budgetManager, sessionAllowedTools,
                               context, cancellationToken))
            {
                yield return evt;
            }

            if (_lastToolState.Stop)
            {
                yield return new AgentRunEvent { Type = AgentRunEventType.Completed };
                yield break;
            }

            // Re-stamp the request with the appended transcript so the
            // next round sees the tool results. ChatRequest is a
            // plain class so `with` doesn't apply; rebuild the
            // record-shaped fields explicitly.
            chatRequest = new ChatRequest
            {
                Model = chatRequest.Model,
                Temperature = chatRequest.Temperature,
                Messages = transcript,
                Tools = chatRequest.Tools
            };
        }
    }

    // One round of "send to model + collect deltas, retrying transient
    // errors". Stashes the final state in _lastChatState so the
    // caller can read it after the IAsyncEnumerable is exhausted.
    private async IAsyncEnumerable<AgentRunEvent> SendChatRoundAsync(
        ChatRequest request,
        AppSettings settings,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _terminalChatEvent = null;
        for (var retryAttempt = 0; retryAttempt <= _retryPolicy.MaxRetries; retryAttempt++)
        {
            yield return new AgentRunEvent { Type = AgentRunEventType.ModelRequestStarted };
            var content = "";
            var reasoning = "";
            var rawEvents = new List<string>();
            var toolCalls = new List<ChatToolCall>();
            var pendingEvents = new List<AgentRunEvent>();
            var hadTransientError = false;
            // 2026-08-05: track the usage block from
            // the most recent delta. OpenAI streaming
            // puts the usage in the final chunk
            // (which may be the [DONE]-preceding
            // chunk or the [DONE] itself, depending
            // on the platform). Some providers emit a
            // separate usage-only chunk between the
            // last content delta and [DONE]; this
            // variable just remembers the most
            // recent non-null value, so the loop end
            // emits a single RunUsage event.
            var lastUsage = (ChatUsage?)null;

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
                        reasoning += delta.ReasoningContent;
                    }

                    if (!string.IsNullOrEmpty(delta.Content))
                    {
                        content += delta.Content;
                    }

                    if (delta.ToolCalls.Count > 0)
                    {
                        toolCalls.AddRange(delta.ToolCalls);
                    }

                    if (delta.Usage is not null)
                    {
                        lastUsage = delta.Usage;
                    }
                }
            }
            catch (HttpRequestException)
            {
                hadTransientError = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // C# forbids yield inside catch / finally; capture
                // the result and emit after the catch block.
                _lastChatState = new ChatRoundState(content, reasoning, toolCalls, ShouldStop: true, Usage: lastUsage);
                _terminalChatEvent = new AgentRunEvent { Type = AgentRunEventType.Cancelled, Content = "Agent 运行已取消。" };
                hadTransientError = false;
            }
            catch (Exception ex)
            {
                _lastChatState = new ChatRoundState(content, reasoning, toolCalls, ShouldStop: true, Usage: lastUsage);
                _terminalChatEvent = new AgentRunEvent { Type = AgentRunEventType.Error, Content = $"LLM 请求失败：{ex.Message}" };
                hadTransientError = false;
            }

            // If a catch block set a terminal event, emit + stop. The
            // two variables stay in sync; the helper exits through
            // the same path regardless of which exception hit.
            if (_terminalChatEvent is { } terminal)
            {
                yield return terminal;
                yield break;
            }

            if (hadTransientError && retryAttempt < _retryPolicy.MaxRetries)
            {
                await Task.Delay(_retryPolicy.GetDelay(retryAttempt), cancellationToken);
                continue;
            }

            if (hadTransientError)
            {
                _lastChatState = new ChatRoundState(content, reasoning, toolCalls, ShouldStop: true, Usage: lastUsage);
                yield return new AgentRunEvent
                {
                    Type = AgentRunEventType.Error,
                    Content = "LLM 请求失败（网络或服务端错误），已重试多次仍无法连接。"
                };
                yield break;
            }

            // Successful round. Flush buffered raw events, then stash
            // the final state for the caller to read.
            foreach (var pending in pendingEvents)
            {
                yield return pending;
            }

            // 2026-08-05: emit a single RunUsage event
            // carrying the platform's per-call token
            // tally + cache hit. The harness forwards
            // it to the runner, which surfaces the
            // cache hit rate in the activity feed. Only
            // emitted when the platform actually
            // attached a usage block — null on legacy
            // providers or stream_options:include_usage
            // not honored.
            if (lastUsage is not null)
            {
                yield return new AgentRunEvent
                {
                    Type = AgentRunEventType.RunUsage,
                    Usage = lastUsage
                };
            }

            _lastChatState = new ChatRoundState(content, reasoning, toolCalls, ShouldStop: toolCalls.Count == 0, Usage: lastUsage);
            yield break;
        }
    }

    // One round of "execute the tool calls the model asked for, mutate
    // the transcript with their results, signal stop if any tool
    // errored / was cancelled / hit the budget".
    private async IAsyncEnumerable<AgentRunEvent> ExecuteToolCallsAsync(
        IReadOnlyList<ChatToolCall> toolCalls,
        List<ChatMessage> transcript,
        AgentBudgetManager budgetManager,
        HashSet<string> sessionAllowedTools,
        AgentRunContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _lastToolState = new ToolRoundState(Stop: false, Cancelled: false);

        foreach (var toolCall in toolCalls)
        {
            var toolRisk = _toolRegistry.Find(toolCall.Name)?.Risk ?? AgentToolRisk.Shell;
            var budgetDecision = budgetManager.ConsumeToolCall(
                toolCall.Name,
                isHighRiskMutation: toolRisk != AgentToolRisk.ReadOnly,
                allowCheckpointPause: false);
            if (budgetDecision.IsHardLimit)
            {
                _lastToolState = new ToolRoundState(Stop: true, Cancelled: false);
                yield return new AgentRunEvent
                {
                    Type = AgentRunEventType.BudgetExceeded,
                    Content = budgetDecision.Reason
                };
                yield break;
            }

            yield return new AgentRunEvent
            {
                Type = AgentRunEventType.ToolCall,
                ToolCall = toolCall
            };

            AgentToolResult? result = null;
            await foreach (var toolEvent in _toolRegistry.ExecuteAsync(
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

            if (result.Status == ToolExecutionStatus.Cancelled)
            {
                _lastToolState = new ToolRoundState(Stop: true, Cancelled: true);
                yield return new AgentRunEvent
                {
                    Type = AgentRunEventType.Cancelled,
                    Content = result.Content
                };
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

    private sealed record ChatRoundState(
        string Content,
        string ReasoningContent,
        IReadOnlyList<ChatToolCall> ToolCalls,
        bool ShouldStop,
        // 2026-08-05: token usage from the final
        // chunk of the model's streaming response.
        // Carries the prompt / completion / cached
        // breakdown that the runner surfaces in the
        // activity feed. Null when the platform
        // didn't attach a usage block (older
        // providers, or stream_options: include_usage
        // not honored).
        ChatUsage? Usage);

    private sealed record ToolRoundState(bool Stop, bool Cancelled);

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
