using AIChat.Abstractions.Configuration;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Context;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Domain.Chat;
using AIChat.Domain.Context;
using AIChat.Domain.Projects;

namespace AIChat.Application.Agents;

public sealed record AgentRequestBuildRequest
{
    public required Conversation Conversation { get; init; }
    public required string AssistantMessageId { get; init; }
    public required AppSettings EffectiveSettings { get; init; }
    public required AppSettings RuntimeSettings { get; init; }
    public string ProjectName { get; init; } = "AIChat";
    public string ProjectPath { get; init; } = "";
    public string WorkspaceBranch { get; init; } = "";
    public int WorkspaceChangeCount { get; init; }
    public IReadOnlyList<PinnedContextItem> PinnedContextItems { get; init; } = [];
    public Dictionary<string, string>? ProjectToolPermissionModes { get; init; }
    public IReadOnlyList<ProjectVerificationCommand> VerificationCommands { get; init; } = [];
    public Func<ToolApprovalRequest, CancellationToken, Task<ToolApprovalDecision>>? RequestToolApprovalAsync { get; init; }
}

public sealed record AgentRequestBuildResult(
    ChatRequest ChatRequest,
    AgentRunContext AgentContext,
    ProjectFileIndex FileIndex,
    string WorkspaceSummary,
    TaskContextPack ContextPack);

public sealed record AgentRequestSnapshot(
    string Provider,
    string Protocol,
    string BaseUrl,
    string Model,
    double Temperature,
    IReadOnlyDictionary<string, string> ModelParameters,
    IReadOnlyList<string> EnabledTools,
    IReadOnlyDictionary<string, ToolPermissionMode> ToolPermissionModes,
    IReadOnlyList<AgentRequestSnapshotMessage> Messages);

public sealed record AgentRequestSnapshotMessage(
    string Id,
    string Role,
    string Content);

public sealed class AgentRequestFactory
{
    private readonly ConversationContextBuilder _contextBuilder;
    private readonly ProjectFileIndexBuilder _fileIndexBuilder;
    private readonly ContextRouter _contextRouter;

    public AgentRequestFactory(
        ConversationContextBuilder contextBuilder,
        ProjectFileIndexBuilder? fileIndexBuilder = null,
        ContextRouter? contextRouter = null)
    {
        _contextBuilder = contextBuilder;
        _fileIndexBuilder = fileIndexBuilder ?? new ProjectFileIndexBuilder();
        _contextRouter = contextRouter ?? new ContextRouter();
    }

    public AgentRequestBuildResult Build(AgentRequestBuildRequest request)
    {
        var projectPath = ResolveProjectPath(request.ProjectPath);
        var fileIndex = _fileIndexBuilder.Build(projectPath);
        var workspaceSummary = !string.IsNullOrWhiteSpace(request.WorkspaceBranch)
            ? $"分支：{request.WorkspaceBranch}，未提交变更：{request.WorkspaceChangeCount} 个文件"
            : "";
        var requestMessages = GetRequestMessages(request.Conversation, request.AssistantMessageId);
        var contextPack = _contextRouter.Route(new ContextRouterRequest
        {
            Goal = requestMessages.LastOrDefault(message => message.Role == ChatRole.User)?.Content ?? "",
            Phase = AgentRunPhase.Executing,
            FileIndex = fileIndex,
            PinnedItems = request.PinnedContextItems,
            ConversationMessages = requestMessages,
            Artifacts = request.Conversation.AgentRuns
                .SelectMany(run => run.Artifacts)
                .OrderByDescending(artifact => artifact.CreatedAt)
                .Take(12)
                .ToList()
        });

        var contextMessages = _contextBuilder.Build(new ConversationContextBuildRequest
        {
            Messages = requestMessages,
            Settings = request.EffectiveSettings,
            PromptContext = new SystemPromptContext
            {
                ProviderId = request.EffectiveSettings.ProviderId,
                ProjectName = string.IsNullOrWhiteSpace(request.ProjectName) ? "AIChat" : request.ProjectName,
                ProjectPath = projectPath,
                EnabledToolIds = request.RuntimeSettings.EnabledToolIds,
                ToolPermissionModes = request.RuntimeSettings.ToolPermissionModes,
                FileIndex = fileIndex,
                WorkspaceSummary = workspaceSummary,
                PinnedContextItems = request.PinnedContextItems,
                ContextRefs = contextPack.ToPromptRefs()
            }
        });

        return new AgentRequestBuildResult(
            new ChatRequest
            {
                Model = request.EffectiveSettings.Model,
                Temperature = request.EffectiveSettings.Temperature,
                Messages = contextMessages
            },
            new AgentRunContext
            {
                ProjectPath = projectPath,
                EnabledToolIds = request.RuntimeSettings.EnabledToolIds,
                ToolPermissionModes = ToolSettingsService.MergePermissionModes(
                    request.RuntimeSettings.ToolPermissionModes,
                    request.ProjectToolPermissionModes),
                MaxToolRounds = request.RuntimeSettings.AgentMaxToolRounds,
                RequestToolApprovalAsync = request.RequestToolApprovalAsync,
                AutoVerifyAgentRuns = request.RuntimeSettings.AutoVerifyAgentRuns,
                MaxAutoFixRounds = request.RuntimeSettings.MaxAutoFixRounds,
                VerificationCommands = request.VerificationCommands
            },
            fileIndex,
            workspaceSummary,
            contextPack);
    }

    public static AgentRequestSnapshot CreateSnapshot(
        Conversation conversation,
        string assistantMessageId,
        AppSettings effectiveSettings,
        AppSettings runtimeSettings,
        IEnumerable<string> enabledToolIds)
    {
        return new AgentRequestSnapshot(
            effectiveSettings.ProviderName,
            effectiveSettings.ProtocolId,
            effectiveSettings.BaseUrl,
            effectiveSettings.Model,
            effectiveSettings.Temperature,
            new Dictionary<string, string>(effectiveSettings.ModelParameters, StringComparer.OrdinalIgnoreCase),
            enabledToolIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            new Dictionary<string, ToolPermissionMode>(runtimeSettings.ToolPermissionModes, StringComparer.OrdinalIgnoreCase),
            GetRequestMessages(conversation, assistantMessageId)
                .Select(message => new AgentRequestSnapshotMessage(
                    message.Id,
                    message.Role.ToString().ToLowerInvariant(),
                    message.Content))
                .ToList());
    }

    private static IReadOnlyList<ChatMessage> GetRequestMessages(Conversation conversation, string assistantMessageId)
    {
        return conversation.Messages
            .Where(message => message.Id != assistantMessageId && !string.IsNullOrWhiteSpace(message.Content))
            .ToList();
    }

    private static string ResolveProjectPath(string projectPath)
    {
        return string.IsNullOrWhiteSpace(projectPath)
            ? Environment.CurrentDirectory
            : projectPath;
    }
}
