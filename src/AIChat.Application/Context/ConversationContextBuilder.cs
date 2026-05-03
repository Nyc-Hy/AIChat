using AIChat.Abstractions.Context;
using AIChat.Application.Prompting;
using AIChat.Domain.Chat;

namespace AIChat.Application.Context;

public sealed class ConversationContextBuilder
{
    private readonly IContextEstimator _contextEstimator;
    private readonly SystemPromptBuilder _systemPromptBuilder;

    public ConversationContextBuilder(IContextEstimator contextEstimator, SystemPromptBuilder systemPromptBuilder)
    {
        _contextEstimator = contextEstimator;
        _systemPromptBuilder = systemPromptBuilder;
    }

    public IReadOnlyList<ChatMessage> Build(ConversationContextBuildRequest request)
    {
        var systemMessage = new ChatMessage
        {
            Role = ChatRole.System,
            Content = _systemPromptBuilder.Build(request.PromptContext),
            CreatedAt = DateTimeOffset.Now
        };
        var sourceMessages = request.Messages
            .Where(IsUsableConversationMessage)
            .Select(CloneMessage)
            .ToList();
        if (sourceMessages.Count == 0)
        {
            return [systemMessage];
        }

        var selected = new List<ChatMessage>();
        foreach (var message in sourceMessages.AsEnumerable().Reverse())
        {
            var candidate = new List<ChatMessage> { systemMessage };
            candidate.AddRange(selected.AsEnumerable().Reverse());
            candidate.Add(message);
            var usage = _contextEstimator.Estimate(candidate, request.Settings);
            if (usage.CurrentTokens > usage.ConversationLimit && selected.Count > 0)
            {
                break;
            }

            selected.Add(message);
        }

        selected.Reverse();
        return [systemMessage, .. selected];
    }

    private static bool IsUsableConversationMessage(ChatMessage message)
    {
        // Tool messages carry tool call results and must be kept so the model
        // sees a complete message sequence (Assistant tool calls + Tool results).
        return message.Role is ChatRole.User or ChatRole.Assistant or ChatRole.System or ChatRole.Tool &&
               !message.IsError &&
               !string.IsNullOrWhiteSpace(message.Content);
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
