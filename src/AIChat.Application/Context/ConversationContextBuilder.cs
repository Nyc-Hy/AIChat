using AIChat.Abstractions.Context;
using AIChat.Application.Prompting;
using AIChat.Domain.Chat;

namespace AIChat.Application.Context;

public sealed class ConversationContextBuilder
{
    private readonly IContextEstimator _contextEstimator;
    private readonly AgentPromptComposer _promptComposer;

    public ConversationContextBuilder(IContextEstimator contextEstimator, SystemPromptBuilder systemPromptBuilder)
        : this(contextEstimator, new AgentPromptComposer(systemPromptBuilder))
    {
    }

    public ConversationContextBuilder(IContextEstimator contextEstimator, AgentPromptComposer promptComposer)
    {
        _contextEstimator = contextEstimator;
        _promptComposer = promptComposer;
    }

    public IReadOnlyList<ChatMessage> Build(ConversationContextBuildRequest request)
    {
        var systemMessage = new ChatMessage
        {
            Role = ChatRole.System,
            Content = _promptComposer.ComposeExecutionSystemMessage(request.PromptContext).Content,
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
               (!string.IsNullOrWhiteSpace(message.Content) || message.ContentParts.Count > 0);
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
