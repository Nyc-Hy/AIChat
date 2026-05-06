using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Domain.Chat;

namespace AIChat.Application.Agents.Planning;

public sealed class AgentPlanner
{
    private readonly IChatCompletionService _chatService;
    private readonly PlannerPromptBuilder _promptBuilder;
    private readonly AgentStructuredPlanParser _parser;

    public AgentPlanner(
        IChatCompletionService chatService,
        PlannerPromptBuilder? promptBuilder = null,
        AgentStructuredPlanParser? parser = null)
    {
        _chatService = chatService;
        _promptBuilder = promptBuilder ?? new PlannerPromptBuilder();
        _parser = parser ?? new AgentStructuredPlanParser();
    }

    public async Task<AgentStructuredPlan> PlanAsync(
        AgentPlanningRequest request,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var content = "";
            await foreach (var delta in _chatService.SendAsync(new ChatRequest
                           {
                               Model = settings.Model,
                               Temperature = Math.Min(settings.Temperature, 0.2),
                               Messages = _promptBuilder.Build(request),
                               Tools = []
                           },
                           settings,
                           cancellationToken))
            {
                content += delta.Content;
            }

            return _parser.ParseOrFallback(content, request);
        }
        catch
        {
            return AgentStructuredPlanParser.CreateFallback(request);
        }
    }
}
