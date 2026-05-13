using AIChat.Abstractions.Configuration;
using AIChat.Application.Artifacts;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Context;
using AIChat.Application.Memory;
using AIChat.Application.Prompting;
using AIChat.Application.Projects;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Domain.Chat;
using AIChat.Domain.Artifacts;
using AIChat.Domain.Context;
using AIChat.Domain.Memory;
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
    public string ProjectLoadSnapshot { get; init; } = "";
    public IReadOnlyList<PinnedContextItem> PinnedContextItems { get; init; } = [];
    public IReadOnlyList<InputArtifact> InputArtifacts { get; init; } = [];
    public IReadOnlyList<MemoryEntry> MemoryEntries { get; init; } = [];
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
    string Content,
    IReadOnlyList<AgentRequestSnapshotContentPart> ContentParts);

public sealed record AgentRequestSnapshotContentPart(
    string Type,
    string Text,
    string MediaType,
    string SourcePath,
    int DataBytes);

public sealed class AgentRequestFactory
{
    private readonly ConversationContextBuilder _contextBuilder;
    private readonly ProjectFileIndexBuilder _fileIndexBuilder;
    private readonly ContextRouter _contextRouter;
    private readonly MemoryRetriever _memoryRetriever;
    private readonly InputArtifactService _inputArtifactService;
    private readonly AgentTaskClassifier _taskClassifier;

    public AgentRequestFactory(
        ConversationContextBuilder contextBuilder,
        ProjectFileIndexBuilder? fileIndexBuilder = null,
        ContextRouter? contextRouter = null,
        MemoryRetriever? memoryRetriever = null,
        InputArtifactService? inputArtifactService = null,
        AgentTaskClassifier? taskClassifier = null)
    {
        _contextBuilder = contextBuilder;
        _fileIndexBuilder = fileIndexBuilder ?? new ProjectFileIndexBuilder();
        _contextRouter = contextRouter ?? new ContextRouter();
        _memoryRetriever = memoryRetriever ?? new MemoryRetriever();
        _inputArtifactService = inputArtifactService ?? new InputArtifactService();
        _taskClassifier = taskClassifier ?? new AgentTaskClassifier();
    }

    public AgentRequestBuildResult Build(AgentRequestBuildRequest request)
    {
        var projectPath = ResolveProjectPath(request.ProjectPath);
        var fileIndex = _fileIndexBuilder.Build(projectPath);
        var workspaceSummary = !string.IsNullOrWhiteSpace(request.WorkspaceBranch)
            ? $"分支：{request.WorkspaceBranch}，未提交变更：{request.WorkspaceChangeCount} 个文件"
            : "";
        var requestMessages = GetRequestMessages(request.Conversation, request.AssistantMessageId);
        var goal = requestMessages.LastOrDefault(message => message.Role == ChatRole.User)?.Content ?? "";
        var taskComplexity = _taskClassifier.Classify(goal, new AgentRunContext
        {
            ProjectPath = projectPath,
            EnabledToolIds = request.RuntimeSettings.EnabledToolIds,
            ToolPermissionModes = request.RuntimeSettings.ToolPermissionModes,
            MaxToolRounds = request.RuntimeSettings.AgentMaxToolRounds
        });
        var memorySnippets = _memoryRetriever.RetrieveSnippets(
            request.MemoryEntries,
            new MemoryRetrievalRequest
            {
                Query = goal,
                MaxResults = taskComplexity == AgentTaskComplexity.Simple ? 2 : 6
            });
        var selectedInputArtifacts = request.InputArtifacts
            .Where(artifact => string.IsNullOrWhiteSpace(artifact.ConversationId) ||
                               string.Equals(artifact.ConversationId, request.Conversation.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(artifact => artifact.CreatedAt)
            .Take(12)
            .ToList();
        var contextPack = _contextRouter.Route(new ContextRouterRequest
        {
            Goal = goal,
            Phase = AgentRunPhase.Executing,
            FileIndex = fileIndex,
            PinnedItems = request.PinnedContextItems,
            ConversationMessages = requestMessages,
            MemorySnippets = memorySnippets,
            Artifacts = request.Conversation.AgentRuns
                .SelectMany(run => run.Artifacts)
                .OrderByDescending(artifact => artifact.CreatedAt)
                .Take(12)
                .ToList(),
            InputArtifacts = selectedInputArtifacts,
            MaxTokens = taskComplexity switch
            {
                AgentTaskComplexity.Simple => 350,
                AgentTaskComplexity.Standard => 900,
                _ => 1600
            },
            MaxFileSizeBytes = taskComplexity == AgentTaskComplexity.Simple
                ? 96 * 1024
                : 256 * 1024
        });
        var inputArtifactRefs = _inputArtifactService.BuildPromptRefs(selectedInputArtifacts, 8);

        var contextMessages = _contextBuilder.Build(new ConversationContextBuildRequest
        {
            Messages = requestMessages,
            Settings = request.EffectiveSettings,
            PromptContext = new SystemPromptContext
            {
                ProviderId = request.EffectiveSettings.ProviderId,
                ProjectName = string.IsNullOrWhiteSpace(request.ProjectName) ? "AIChat" : request.ProjectName,
                ProjectPath = projectPath,
                ProjectLoadSnapshot = string.IsNullOrWhiteSpace(request.ProjectLoadSnapshot)
                    ? BuildProjectLoadSnapshotFallback(request, projectPath)
                    : request.ProjectLoadSnapshot,
                EnabledToolIds = request.RuntimeSettings.EnabledToolIds,
                ToolPermissionModes = request.RuntimeSettings.ToolPermissionModes,
                FileIndex = fileIndex,
                WorkspaceSummary = workspaceSummary,
                PinnedContextItems = request.PinnedContextItems,
                ContextRefs = contextPack.ToPromptRefs(),
                InputArtifactRefs = inputArtifactRefs
            }
        });
        if (SupportsVision(request.EffectiveSettings))
        {
            AttachImageArtifactsToLatestUserMessage(contextMessages, selectedInputArtifacts);
        }

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
                AdaptiveStrategiesEnabled = request.RuntimeSettings.AgentAdaptiveStrategiesEnabled,
                AdaptiveBudgetAndExplorerEnabled = request.RuntimeSettings.AgentAdaptiveBudgetAndExplorerEnabled,
                AdaptiveRecoveryEnabled = request.RuntimeSettings.AgentAdaptiveRecoveryEnabled,
                AdaptiveAutoVerifyEnabled = request.RuntimeSettings.AgentAdaptiveAutoVerifyEnabled,
                VerificationCommands = request.VerificationCommands,
                InputArtifacts = selectedInputArtifacts
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
                .Select(ToSnapshotMessage)
                .ToList());
    }

    public static AgentRequestSnapshot CreateSnapshot(
        ChatRequest chatRequest,
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
            chatRequest.Messages.Select(ToSnapshotMessage).ToList());
    }

    private static AgentRequestSnapshotMessage ToSnapshotMessage(ChatMessage message)
    {
        return new AgentRequestSnapshotMessage(
            message.Id,
            message.Role.ToString().ToLowerInvariant(),
            message.Content,
            message.ContentParts.Select(ToSnapshotContentPart).ToList());
    }

    private static AgentRequestSnapshotContentPart ToSnapshotContentPart(ChatContentPart part)
    {
        return new AgentRequestSnapshotContentPart(
            part.Type,
            string.Equals(part.Type, "text", StringComparison.OrdinalIgnoreCase)
                ? part.Text
                : "",
            part.MediaType,
            part.SourcePath,
            string.IsNullOrWhiteSpace(part.DataBase64)
                ? 0
                : Convert.FromBase64String(part.DataBase64).Length);
    }

    private static IReadOnlyList<ChatMessage> GetRequestMessages(Conversation conversation, string assistantMessageId)
    {
        return conversation.Messages
            .Where(message => message.Id != assistantMessageId &&
                              (!string.IsNullOrWhiteSpace(message.Content) || message.ContentParts.Count > 0))
            .ToList();
    }

    private static void AttachImageArtifactsToLatestUserMessage(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<InputArtifact> artifacts)
    {
        var latestUser = messages.LastOrDefault(message => message.Role == ChatRole.User);
        if (latestUser is null)
        {
            return;
        }

        foreach (var part in BuildImageContentParts(artifacts))
        {
            latestUser.ContentParts.Add(part);
        }
    }

    private static IReadOnlyList<ChatContentPart> BuildImageContentParts(IReadOnlyList<InputArtifact> artifacts)
    {
        var parts = new List<ChatContentPart>();
        foreach (var decision in InputArtifactVisionPolicy.Evaluate(artifacts, modelSupportsVision: true)
                     .Where(decision => decision.CanSend))
        {
            var data = Convert.ToBase64String(File.ReadAllBytes(decision.StoredPath));
            parts.Add(ChatContentPart.ImagePart(decision.MediaType, data, decision.StoredPath));
        }

        return parts;
    }

    private static bool SupportsVision(AppSettings settings)
    {
        return settings.ModelSupportsVision;
    }

    private static string BuildProjectLoadSnapshotFallback(AgentRequestBuildRequest request, string projectPath)
    {
        var snapshot = ProjectLoadSnapshotBuilder.Build(new ProjectWorkspace
        {
            Name = string.IsNullOrWhiteSpace(request.ProjectName) ? "AIChat" : request.ProjectName,
            Path = projectPath,
            Conversations = [request.Conversation],
            VerificationCommands = request.VerificationCommands.ToList()
        });
        return string.Join(Environment.NewLine, [
            snapshot.HealthText,
            snapshot.ProfileText,
            snapshot.ActivityText,
            snapshot.RecommendationText
        ]);
    }

    private static string ResolveProjectPath(string projectPath)
    {
        return string.IsNullOrWhiteSpace(projectPath)
            ? Environment.CurrentDirectory
            : projectPath;
    }
}
