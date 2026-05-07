using AIChat.Application.Prompting;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.Planning;

public sealed class PlannerPromptBuilder
{
    private readonly AgentPromptComposer _promptComposer;

    public PlannerPromptBuilder(AgentPromptComposer? promptComposer = null)
    {
        _promptComposer = promptComposer ?? new AgentPromptComposer();
    }

    public IReadOnlyList<ChatMessage> Build(AgentPlanningRequest request)
    {
        return _promptComposer.Compose(new AgentPromptComposeRequest
        {
            Profile = AgentPromptProfile.Planning,
            Goal = request.Goal,
            AllowedTools = request.EnabledToolIds,
            ContextRefs = string.IsNullOrWhiteSpace(request.ProjectPath) ? [] : [request.ProjectPath],
            ConversationMessages = request.Messages
        }).Messages;
    }
}
