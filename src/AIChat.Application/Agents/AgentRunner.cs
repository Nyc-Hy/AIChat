using System.Runtime.CompilerServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
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

    public AgentRunner(IChatCompletionService chatService, AgentToolCatalog toolCatalog)
        : this(chatService, toolCatalog, new ToolExecutionService(toolCatalog))
    {
    }

    public AgentRunner(
        IChatCompletionService chatService,
        AgentToolCatalog toolCatalog,
        ToolExecutionService toolExecutionService)
    {
        _chatService = chatService;
        _toolCatalog = toolCatalog;
        _toolExecutionService = toolExecutionService;
    }

    public async IAsyncEnumerable<AgentRunEvent> RunAsync(
        ChatRequest initialRequest,
        AppSettings settings,
        AgentRunContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enabledTools = _toolCatalog.ResolveEnabled(context.EnabledToolIds);
        var requiresProjectMutation = RequiresProjectMutation(initialRequest.Messages);
        var hasMutationToolResult = false;
        var sessionAllowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transcript = initialRequest.Messages
            .Select(CloneMessage)
            .ToList();
        AgentToolResult? lastToolResult = null;

        for (var round = 0; round < context.MaxToolRounds; round++)
        {
            var request = new ChatRequest
            {
                Model = initialRequest.Model,
                Temperature = initialRequest.Temperature,
                Messages = transcript,
                Tools = enabledTools.Select(tool => tool.Definition).ToList()
            };

            var assistantContent = "";
            var rawEvents = new List<string>();
            var requestedToolCalls = new List<ChatToolCall>();

            await foreach (var delta in _chatService.SendAsync(request, settings, cancellationToken))
            {
                if (!string.IsNullOrWhiteSpace(delta.RawJson))
                {
                    rawEvents.Add(delta.RawJson);
                    yield return new AgentRunEvent
                    {
                        Type = AgentRunEventType.RawProviderEvent,
                        RawJson = delta.RawJson
                    };
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

            if (requestedToolCalls.Count == 0)
            {
                if (requiresProjectMutation && !hasMutationToolResult && enabledTools.Any(tool => tool.Risk != AgentToolRisk.ReadOnly))
                {
                    transcript.Add(new ChatMessage
                    {
                        Role = ChatRole.Assistant,
                        Content = assistantContent,
                        CreatedAt = DateTimeOffset.Now
                    });
                    transcript.Add(new ChatMessage
                    {
                        Role = ChatRole.User,
                        Content = "你刚才没有调用任何工具，但用户要求创建或修改当前项目。不要声称已经完成。请先用 read_file/list_files/search_text 检查项目，再调用 write_file 或 edit_file 实际修改文件；如果需要执行命令，再调用 run_shell。",
                        CreatedAt = DateTimeOffset.Now
                    });
                    continue;
                }

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
                            if (toolEvent.IsMutation)
                            {
                                hasMutationToolResult = true;
                            }
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

                transcript.Add(new ChatMessage
                {
                    Role = ChatRole.Tool,
                    ToolCallId = toolCall.Id,
                    ToolName = toolCall.Name,
                    Content = result.Content,
                    CreatedAt = DateTimeOffset.Now
                });
            }
        }

        yield return new AgentRunEvent
        {
            Type = AgentRunEventType.BudgetExceeded,
            Content = lastToolResult is null
                ? "\n\n已达到工具调用轮数上限，请缩小问题范围后再试。"
                : $"\n\n已达到工具调用轮数上限。最后一次工具结果如下，供你判断问题：\n\n```json\n{lastToolResult.Content}\n```"
        };
        yield return new AgentRunEvent { Type = AgentRunEventType.Completed };
    }

    private static bool RequiresProjectMutation(IReadOnlyList<ChatMessage> messages)
    {
        var latestUser = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Content ?? "";
        if (string.IsNullOrWhiteSpace(latestUser))
        {
            return false;
        }

        var mutationWords = new[]
        {
            "创建", "新建", "生成", "实现", "写一个", "做一个", "加一个", "新增",
            "修改", "改成", "改为", "替换", "删除", "修复", "优化", "重构",
            "create", "implement", "write", "modify", "change", "replace", "fix", "update", "add"
        };
        return mutationWords.Any(word => latestUser.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private static ChatMessage CloneMessage(ChatMessage message)
    {
        return new ChatMessage
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            Role = message.Role,
            Content = message.Content,
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
}
