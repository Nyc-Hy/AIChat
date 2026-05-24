using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;
using AIChat.Application.Agents;
using AIChat.Application.Agents.Planning;
using AIChat.Application.Configuration;
using AIChat.Application.Context;
using AIChat.Application.Agents.Coordinator;
using AIChat.Application.Llm.Routing;
using AIChat.Application.Projects;
using AIChat.Application.Prompting;
using AIChat.Application.Tools;
using AIChat.Application.Workspace;
using AIChat.Domain.Chat;
using AIChat.Domain.Projects;
using AIChat.Providers.Anthropic;
using AIChat.Providers.OpenAI;
using AIChat.Storage.Json;
using System.Reflection;

var command = CliCommand.Parse(args);
if (command.ShowHelp)
{
    PrintHelp();
    return command.HasError ? 1 : 0;
}

var repository = string.IsNullOrWhiteSpace(command.DataDirectory)
    ? new JsonAppRepository()
    : new JsonAppRepository(command.DataDirectory);
var settings = await repository.LoadSettingsAsync();
var toolRegistry = AgentToolRegistry.CreateDefault();

ProviderSettingsService.Normalize(settings, settings.Temperature);
AdvancedSettingsService.Normalize(settings);
ToolSettingsService.Normalize(settings, toolRegistry);

switch (command.Name)
{
    case "version":
    case "--version":
        PrintVersion();
        return 0;
    case "doctor":
        return await RunDoctorAsync(command, repository, settings, toolRegistry);
    case "models":
        PrintModels(command);
        return 0;
    case "config":
        return await RunConfigAsync(command, repository, settings);
    case "projects":
        return await RunProjectsAsync(command, repository);
    case "context":
        return await RunContextAsync(command, repository);
    case "init":
        return await RunInitAsync(command, repository);
    case "ask":
        return await RunAskAsync(command, repository, settings, toolRegistry);
    case "tui":
        return await RunTuiAsync(command, repository, settings, toolRegistry);
    default:
        Console.Error.WriteLine($"Unknown command: {command.Name}");
        PrintHelp();
        return 1;
}

static async Task<int> RunConfigAsync(
    CliCommand command,
    JsonAppRepository repository,
    AppSettings settings)
{
    var action = command.Positionals.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(action) || string.Equals(action, "show", StringComparison.OrdinalIgnoreCase))
    {
        PrintActiveConfig(settings);
        return 0;
    }

    if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
    {
        PrintConfiguredProviders(settings);
        return 0;
    }

    if (string.Equals(action, "use", StringComparison.OrdinalIgnoreCase))
    {
        var selectedProviderId = command.GetOption("provider");
        var selectedModelId = command.GetOption("model");
        if (string.IsNullOrWhiteSpace(selectedProviderId))
        {
            Console.Error.WriteLine("config use requires --provider.");
            return 1;
        }

        var selected = settings.ConfiguredProviders.FirstOrDefault(provider =>
            string.Equals(provider.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider.TemplateId, selectedProviderId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(provider.Name, selectedProviderId, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            Console.Error.WriteLine($"Configured provider was not found: {selectedProviderId}");
            return 1;
        }

        settings.ActiveConfiguredProviderId = selected.Id;
        if (!string.IsNullOrWhiteSpace(selectedModelId))
        {
            selected.SelectedModelId = ChatProviderCatalog.ResolveModel(selected.TemplateId, selectedModelId).Id;
        }

        ProviderSettingsService.ApplySelectedProvider(settings);
        await repository.SaveSettingsAsync(settings);
        Console.WriteLine($"Active provider: {selected.Name} ({selected.SelectedModelId})");
        return 0;
    }

    if (!string.Equals(action, "set-provider", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Expected config action: show, list, use, or set-provider.");
        return 1;
    }

    var providerId = command.GetOption("provider");
    var apiKey = command.GetOption("api-key");
    var modelId = command.GetOption("model");
    var baseUrl = command.GetOption("base-url");
    if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(apiKey))
    {
        Console.Error.WriteLine("config set-provider requires --provider and --api-key.");
        return 1;
    }

    var result = ProviderSettingsService.AddConfiguredProvider(settings, providerId, apiKey);
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        result.Provider.BaseUrl = baseUrl.Trim();
    }

    if (!string.IsNullOrWhiteSpace(modelId))
    {
        result.Provider.SelectedModelId = ChatProviderCatalog.ResolveModel(result.Provider.TemplateId, modelId).Id;
    }

    settings.ActiveConfiguredProviderId = result.Provider.Id;
    ProviderSettingsService.ApplySelectedProvider(settings);
    await repository.SaveSettingsAsync(settings);
    Console.WriteLine(result.AlreadyExisted
        ? $"Updated provider: {result.Provider.Name} ({result.Provider.SelectedModelId})"
        : $"Added provider: {result.Provider.Name} ({result.Provider.SelectedModelId})");
    return 0;
}

static async Task<int> RunDoctorAsync(
    CliCommand command,
    JsonAppRepository repository,
    AppSettings settings,
    AgentToolRegistry toolRegistry)
{
    Console.WriteLine($"AIChat CLI: {GetVersion()}");
    Console.WriteLine($".NET: {Environment.Version}");
    Console.WriteLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
    Console.WriteLine($"Data directory: {(string.IsNullOrWhiteSpace(command.DataDirectory) ? "(platform default)" : command.DataDirectory)}");

    var active = ProviderSettingsService.GetSelectedProvider(settings);
    Console.WriteLine(active is null
        ? "Provider: not configured"
        : $"Provider: {active.Name} ({active.SelectedModelId})");
    Console.WriteLine($"Tools: {toolRegistry.All.Count}");

    var projects = await repository.LoadProjectsAsync();
    var configuredProjects = projects.Count(project => !string.IsNullOrWhiteSpace(project.Path));
    Console.WriteLine($"Projects: {configuredProjects}");

    var ok = active is not null && !string.IsNullOrWhiteSpace(active.ApiKey);
    Console.WriteLine(ok ? "Status: ready" : "Status: needs provider configuration");
    return ok ? 0 : 2;
}

static async Task<int> RunProjectsAsync(CliCommand command, JsonAppRepository repository)
{
    var action = command.Positionals.FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(action) && !string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Expected projects action: list.");
        return 1;
    }

    var projects = await repository.LoadProjectsAsync();
    foreach (var project in projects.Where(project => !string.IsNullOrWhiteSpace(project.Path)))
    {
        Console.WriteLine($"{project.Name}");
        Console.WriteLine($"  path: {project.Path}");
        Console.WriteLine($"  conversations: {project.Conversations.Count}");
        Console.WriteLine($"  verification commands: {project.VerificationCommands.Count}");
    }

    return 0;
}

static async Task<int> RunContextAsync(CliCommand command, JsonAppRepository repository)
{
    var projectPath = ResolveProjectPath(command.GetOption("project"));
    if (!Directory.Exists(projectPath))
    {
        Console.Error.WriteLine($"Project directory does not exist: {projectPath}");
        return 1;
    }

    var projects = (await repository.LoadProjectsAsync()).ToList();
    var project = FindProject(projects, projectPath) ?? new ProjectWorkspace
    {
        Name = new DirectoryInfo(projectPath).Name,
        Path = projectPath,
        VerificationCommands = new ProjectInitializer().SuggestVerificationCommands(projectPath).ToList()
    };

    var goal = string.Join(' ', command.Positionals).Trim();
    if (string.IsNullOrWhiteSpace(goal))
    {
        goal = "project overview";
    }

    var maxTokens = ParsePositiveInt(command.GetOption("tokens"), 1200);
    var maxFiles = ParsePositiveInt(command.GetOption("max-files"), 500);
    var fileIndex = new ProjectFileIndexBuilder().Build(projectPath, maxFiles);
    var contextPack = new ContextRouter().Route(new ContextRouterRequest
    {
        Goal = goal,
        Phase = AgentRunPhase.GatheringContext,
        FileIndex = fileIndex,
        PinnedItems = project.PinnedContext,
        InputArtifacts = project.InputArtifacts,
        MemorySnippets = project.Memories.Select(memory => memory.Content).ToList(),
        MaxTokens = maxTokens
    });

    PrintContextReport(project, fileIndex, contextPack, goal, maxTokens, maxFiles);
    return 0;
}

static async Task<int> RunInitAsync(CliCommand command, JsonAppRepository repository)
{
    var projectPath = ResolveProjectPath(command.GetOption("project"));
    if (!Directory.Exists(projectPath))
    {
        Console.Error.WriteLine($"Project directory does not exist: {projectPath}");
        return 1;
    }

    var projects = (await repository.LoadProjectsAsync()).ToList();
    var project = FindProject(projects, projectPath);
    if (project is null)
    {
        project = new ProjectWorkspace
        {
            Name = command.GetOption("name") is { Length: > 0 } name
                ? name
                : new DirectoryInfo(projectPath).Name,
            Path = projectPath,
            UpdatedAt = DateTimeOffset.Now
        };
        project.VerificationCommands = new ProjectInitializer()
            .SuggestVerificationCommands(projectPath)
            .ToList();
        projects.Add(project);
    }
    else
    {
        project.Name = command.GetOption("name") is { Length: > 0 } name
            ? name
            : project.Name;
        project.Path = projectPath;
        project.UpdatedAt = DateTimeOffset.Now;
        if (project.VerificationCommands.Count == 0)
        {
            project.VerificationCommands = new ProjectInitializer()
                .SuggestVerificationCommands(projectPath)
                .ToList();
        }
    }

    await repository.SaveProjectsAsync(projects);
    Console.WriteLine($"Initialized project: {project.Name}");
    Console.WriteLine(project.Path);
    Console.WriteLine(project.VerificationCommands.Count == 0
        ? "No verification commands detected."
        : $"Detected verification commands: {project.VerificationCommands.Count}");
    return 0;
}

static async Task<int> RunAskAsync(
    CliCommand command,
    JsonAppRepository repository,
    AppSettings settings,
    AgentToolRegistry toolRegistry)
{
    var prompt = string.Join(' ', command.Positionals).Trim();
    if (string.IsNullOrWhiteSpace(prompt))
    {
        Console.Error.WriteLine("ask requires a prompt.");
        return 1;
    }

    var effectiveSettings = ProviderSettingsService.CreateEffectiveSettings(settings, settings.Temperature);
    if (effectiveSettings is null)
    {
        Console.Error.WriteLine("No configured provider with an API key was found.");
        Console.Error.WriteLine("Run: aichat config set-provider --provider deepseek --api-key <key>");
        return 2;
    }

    var mode = command.GetOption("mode") is { Length: > 0 } modeName
        ? AgentExecutionModePolicy.Parse(modeName)
        : settings.AgentExecutionMode;
    var modeSettings = ApplyCliRuntimeDefaults(settings, mode);
    if (command.HasFlag("verify"))
    {
        settings.AutoVerifyAgentRuns = true;
        settings.MaxAutoFixRounds = Math.Max(settings.MaxAutoFixRounds, 1);
    }

    var projectPath = ResolveProjectPath(command.GetOption("project"));
    var projects = (await repository.LoadProjectsAsync()).ToList();
    var project = FindProject(projects, projectPath);
    if (project is null)
    {
        project = new ProjectWorkspace
        {
            Name = new DirectoryInfo(projectPath).Name,
            Path = projectPath,
            VerificationCommands = new ProjectInitializer().SuggestVerificationCommands(projectPath).ToList()
        };
        projects.Add(project);
    }

    var conversation = new Conversation
    {
        ProjectId = project.Id,
        Title = prompt.Length > 80 ? prompt[..80] : prompt,
        UpdatedAt = DateTimeOffset.Now
    };
    var userMessage = new ChatMessage
    {
        ConversationId = conversation.Id,
        Role = ChatRole.User,
        Content = prompt,
        CreatedAt = DateTimeOffset.Now
    };
    var assistantMessage = new ChatMessage
    {
        ConversationId = conversation.Id,
        Role = ChatRole.Assistant,
        Content = "",
        CreatedAt = DateTimeOffset.Now
    };
    conversation.Messages.Add(userMessage);
    conversation.Messages.Add(assistantMessage);

    var chatService = new RoutedChatCompletionService(
    [
        new OpenAICompatibleChatProvider(),
        new AnthropicChatProvider()
    ]);
    var requestFactory = new AgentRequestFactory(
        new ConversationContextBuilder(
            new TokenizerContextEstimator(),
            new SystemPromptBuilder()));
    if (command.HasFlag("no-write"))
    {
        DisableMutationTools(settings);
    }

    var requestBuild = requestFactory.Build(new AgentRequestBuildRequest
    {
        Conversation = conversation,
        AssistantMessageId = assistantMessage.Id,
        EffectiveSettings = effectiveSettings,
        RuntimeSettings = settings,
        ProjectName = project.Name,
        ProjectPath = project.Path,
        ProjectLoadSnapshot = BuildProjectSnapshot(project),
        PinnedContextItems = project.PinnedContext,
        InputArtifacts = project.InputArtifacts,
        MemoryEntries = project.Memories,
        ProjectToolPermissionModes = project.ProjectToolPermissionModes,
        VerificationCommands = project.VerificationCommands,
        RequestToolApprovalAsync = command.HasFlag("yes")
            ? ApproveToolAsync
            : RejectToolAsync
    });

    var supportsTools = ChatProviderCatalog.ResolveModel(effectiveSettings.ProviderId, effectiveSettings.Model)
        .Capabilities.SupportsTools;
    var output = command.HasFlag("plain") || !supportsTools
        ? await RunPlainChatAsync(chatService, requestBuild.ChatRequest, effectiveSettings)
        : await RunAgentAsync(
            new AgentHarness(
                new AgentRunner(chatService, new AgentToolCatalog(toolRegistry.All)),
                modeSettings.EnablePlanner ? new AgentPlanner(chatService) : null),
            requestBuild,
            effectiveSettings,
            conversation,
            userMessage.Id,
            assistantMessage.Id,
            prompt);

    assistantMessage.Content = output;
    conversation.UpdatedAt = DateTimeOffset.Now;
    project.Conversations.Add(conversation);
    project.UpdatedAt = DateTimeOffset.Now;
    await repository.SaveProjectsAsync(projects);
    return 0;
}

static async Task<int> RunTuiAsync(
    CliCommand command,
    JsonAppRepository repository,
    AppSettings settings,
    AgentToolRegistry toolRegistry)
{
    var effectiveSettings = ProviderSettingsService.CreateEffectiveSettings(settings, settings.Temperature);
    if (effectiveSettings is null)
    {
        Console.Error.WriteLine("No configured provider with an API key was found.");
        Console.Error.WriteLine("Run: aichat config set-provider --provider deepseek --api-key <key>");
        return 2;
    }

    var mode = command.GetOption("mode") is { Length: > 0 } modeName
        ? AgentExecutionModePolicy.Parse(modeName)
        : settings.AgentExecutionMode;
    var plain = command.HasFlag("plain");
    var autoApprove = command.HasFlag("yes");
    var noWrite = command.HasFlag("no-write");
    var verify = command.HasFlag("verify");
    var projectPath = ResolveProjectPath(command.GetOption("project"));
    var projects = (await repository.LoadProjectsAsync()).ToList();
    var project = FindProject(projects, projectPath);
    if (project is null)
    {
        project = new ProjectWorkspace
        {
            Name = new DirectoryInfo(projectPath).Name,
            Path = projectPath,
            VerificationCommands = new ProjectInitializer().SuggestVerificationCommands(projectPath).ToList()
        };
        projects.Add(project);
    }

    var conversation = new Conversation
    {
        ProjectId = project.Id,
        Title = $"TUI {DateTimeOffset.Now:yyyy-MM-dd HH:mm}",
        UpdatedAt = DateTimeOffset.Now
    };
    project.Conversations.Add(conversation);
    await repository.SaveProjectsAsync(projects);

    var chatService = new RoutedChatCompletionService(
    [
        new OpenAICompatibleChatProvider(),
        new AnthropicChatProvider()
    ]);
    var requestFactory = new AgentRequestFactory(
        new ConversationContextBuilder(
            new TokenizerContextEstimator(),
            new SystemPromptBuilder()));

    Console.WriteLine("AIChat TUI Beta");
    Console.WriteLine($"Project: {project.Name}");
    Console.WriteLine($"Model: {effectiveSettings.ProviderName} / {effectiveSettings.Model}");
    Console.WriteLine($"Mode: {mode}");
    Console.WriteLine("Commands: /help, /mode fast|standard|deep, /yes, /plain, /no-write, /verify, /status, /exit");
    Console.WriteLine();

    while (true)
    {
        Console.Write("> ");
        var input = Console.ReadLine();
        if (input is null)
        {
            break;
        }

        var prompt = input.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            continue;
        }

        if (TryHandleTuiCommand(prompt, ref mode, ref plain, ref autoApprove, ref noWrite, ref verify, effectiveSettings, project))
        {
            continue;
        }

        var modeSettings = ApplyCliRuntimeDefaults(settings, mode);
        if (verify)
        {
            settings.AutoVerifyAgentRuns = true;
            settings.MaxAutoFixRounds = Math.Max(settings.MaxAutoFixRounds, 1);
        }

        if (noWrite)
        {
            DisableMutationTools(settings);
        }

        var userMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRole.User,
            Content = prompt,
            CreatedAt = DateTimeOffset.Now
        };
        var assistantMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            Role = ChatRole.Assistant,
            Content = "",
            CreatedAt = DateTimeOffset.Now
        };
        conversation.Messages.Add(userMessage);
        conversation.Messages.Add(assistantMessage);

        var requestBuild = requestFactory.Build(new AgentRequestBuildRequest
        {
            Conversation = conversation,
            AssistantMessageId = assistantMessage.Id,
            EffectiveSettings = effectiveSettings,
            RuntimeSettings = settings,
            ProjectName = project.Name,
            ProjectPath = project.Path,
            ProjectLoadSnapshot = BuildProjectSnapshot(project),
            PinnedContextItems = project.PinnedContext,
            InputArtifacts = project.InputArtifacts,
            MemoryEntries = project.Memories,
            ProjectToolPermissionModes = project.ProjectToolPermissionModes,
            VerificationCommands = project.VerificationCommands,
            RequestToolApprovalAsync = autoApprove ? ApproveToolAsync : InteractiveToolApprovalAsync
        });

        var supportsTools = ChatProviderCatalog.ResolveModel(effectiveSettings.ProviderId, effectiveSettings.Model)
            .Capabilities.SupportsTools;
        Console.WriteLine();
        var output = plain || !supportsTools
            ? await RunPlainChatAsync(chatService, requestBuild.ChatRequest, effectiveSettings)
            : await RunAgentAsync(
                new AgentHarness(
                    new AgentRunner(chatService, new AgentToolCatalog(toolRegistry.All)),
                    modeSettings.EnablePlanner ? new AgentPlanner(chatService) : null),
                requestBuild,
                effectiveSettings,
                conversation,
                userMessage.Id,
                assistantMessage.Id,
                prompt);

        assistantMessage.Content = output;
        conversation.UpdatedAt = DateTimeOffset.Now;
        project.UpdatedAt = DateTimeOffset.Now;
        await repository.SaveProjectsAsync(projects);
        Console.WriteLine();
    }

    return 0;
}

static bool TryHandleTuiCommand(
    string input,
    ref AgentExecutionMode mode,
    ref bool plain,
    ref bool autoApprove,
    ref bool noWrite,
    ref bool verify,
    AppSettings effectiveSettings,
    ProjectWorkspace project)
{
    if (!input.StartsWith("/", StringComparison.Ordinal))
    {
        return false;
    }

    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var command = parts[0].ToLowerInvariant();
    switch (command)
    {
        case "/exit":
        case "/quit":
            Environment.Exit(0);
            return true;
        case "/help":
            Console.WriteLine("Commands:");
            Console.WriteLine("  /mode fast|standard|deep");
            Console.WriteLine("  /yes       toggle automatic approval for this TUI session");
            Console.WriteLine("  /plain     toggle plain chat mode");
            Console.WriteLine("  /no-write  toggle read-only/no-mutation tools");
            Console.WriteLine("  /verify    toggle automatic verification when commands exist");
            Console.WriteLine("  /status    print current session status");
            Console.WriteLine("  /exit      leave TUI");
            return true;
        case "/mode":
            if (parts.Length < 2)
            {
                Console.WriteLine($"Mode: {mode}");
                return true;
            }

            mode = AgentExecutionModePolicy.Parse(parts[1]);
            Console.WriteLine($"Mode: {mode}");
            return true;
        case "/yes":
            autoApprove = !autoApprove;
            Console.WriteLine($"Auto approve: {autoApprove}");
            return true;
        case "/plain":
            plain = !plain;
            Console.WriteLine($"Plain chat: {plain}");
            return true;
        case "/no-write":
            noWrite = !noWrite;
            Console.WriteLine($"No write: {noWrite}");
            return true;
        case "/verify":
            verify = !verify;
            Console.WriteLine($"Auto verify: {verify}");
            return true;
        case "/status":
            Console.WriteLine($"Project: {project.Name}");
            Console.WriteLine($"Path: {project.Path}");
            Console.WriteLine($"Model: {effectiveSettings.ProviderName} / {effectiveSettings.Model}");
            Console.WriteLine($"Mode: {mode}");
            Console.WriteLine($"Plain: {plain}; Auto approve: {autoApprove}; No write: {noWrite}; Verify: {verify}");
            Console.WriteLine($"Verification commands: {project.VerificationCommands.Count}");
            return true;
        default:
            Console.WriteLine($"Unknown TUI command: {parts[0]}");
            return true;
    }
}

static AgentExecutionModeSettings ApplyCliRuntimeDefaults(AppSettings settings, AgentExecutionMode mode)
{
    AgentExecutionModePolicy.Apply(settings, mode);
    return AgentExecutionModePolicy.Resolve(mode);
}

static void DisableMutationTools(AppSettings settings)
{
    var disabled = new[] { "write_file", "edit_file", "apply_patch", "git_restore_file", "git_commit", "run_build", "run_test", "run_shell" };
    settings.EnabledToolIds = settings.EnabledToolIds
        .Where(toolId => !disabled.Contains(toolId, StringComparer.OrdinalIgnoreCase))
        .ToList();
    foreach (var toolId in disabled)
    {
        settings.ToolPermissionModes[toolId] = ToolPermissionMode.Disabled;
    }
}

static async Task<string> RunPlainChatAsync(
    RoutedChatCompletionService chatService,
    ChatRequest request,
    AppSettings settings)
{
    var output = new System.Text.StringBuilder();
    await foreach (var delta in chatService.SendAsync(request, settings))
    {
        if (!string.IsNullOrEmpty(delta.Content))
        {
            Console.Write(delta.Content);
            output.Append(delta.Content);
        }
    }

    Console.WriteLine();
    return output.ToString();
}

static async Task<string> RunAgentAsync(
    AgentHarness harness,
    AgentRequestBuildResult requestBuild,
    AppSettings settings,
    Conversation conversation,
    string userMessageId,
    string assistantMessageId,
    string goal)
{
    var output = new System.Text.StringBuilder();
    await foreach (var agentEvent in harness.RunAsync(new AgentHarnessRunRequest
                   {
                       Conversation = conversation,
                       UserMessageId = userMessageId,
                       AssistantMessageId = assistantMessageId,
                       Goal = goal,
                       ChatRequest = requestBuild.ChatRequest,
                       Settings = settings,
                       ContextPack = requestBuild.ContextPack,
                       Context = requestBuild.AgentContext
                   }))
    {
        switch (agentEvent.Type)
        {
            case AgentHarnessEventType.PhaseChanged:
                if (!string.IsNullOrWhiteSpace(agentEvent.PhaseTransition?.Summary))
                {
                    Console.Error.WriteLine($"[{agentEvent.PhaseTransition.Phase}] {agentEvent.PhaseTransition.Summary}");
                }

                break;
            case AgentHarnessEventType.ToolCall:
                Console.Error.WriteLine($"[tool] {agentEvent.ToolCall?.Name}");
                break;
            case AgentHarnessEventType.ToolApprovalRejected:
                Console.Error.WriteLine($"[tool rejected] {agentEvent.ToolCall?.Name}");
                break;
            case AgentHarnessEventType.ToolResult:
                var status = agentEvent.ToolResult?.IsError == true ? "failed" : "ok";
                Console.Error.WriteLine($"[tool {status}] {agentEvent.ToolResult?.ToolName}");
                break;
            case AgentHarnessEventType.ContentDelta:
                Console.Write(agentEvent.Content);
                output.Append(agentEvent.Content);
                break;
        }
    }

    Console.WriteLine();
    return output.ToString();
}

static Task<ToolApprovalDecision> ApproveToolAsync(
    ToolApprovalRequest request,
    CancellationToken cancellationToken)
{
    Console.Error.WriteLine($"[approve] {request.ToolCall.Name}: {request.Preview.Summary}");
    return Task.FromResult(ToolApprovalDecision.Approve(allowForSession: true));
}

static Task<ToolApprovalDecision> RejectToolAsync(
    ToolApprovalRequest request,
    CancellationToken cancellationToken)
{
    Console.Error.WriteLine($"[needs --yes] {request.ToolCall.Name}: {request.Preview.Summary}");
    return Task.FromResult(ToolApprovalDecision.Reject("CLI requires --yes for write, build, test, shell, and git mutation tools."));
}

static Task<ToolApprovalDecision> InteractiveToolApprovalAsync(
    ToolApprovalRequest request,
    CancellationToken cancellationToken)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"[approval required] {request.ToolCall.Name}");
    Console.Error.WriteLine(request.Preview.Summary);
    if (!string.IsNullOrWhiteSpace(request.Preview.PreviewText))
    {
        Console.Error.WriteLine(request.Preview.PreviewText);
    }

    if (!string.IsNullOrWhiteSpace(request.Preview.DiffText))
    {
        Console.Error.WriteLine(request.Preview.DiffText);
    }

    Console.Error.Write("Approve? [y]es / [s]ession / [n]o: ");
    var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
    return Task.FromResult(answer switch
    {
        "y" or "yes" => ToolApprovalDecision.Approve(),
        "s" or "session" => ToolApprovalDecision.Approve(allowForSession: true),
        _ => ToolApprovalDecision.Reject("Rejected in TUI approval prompt.")
    });
}

static void PrintModels(CliCommand command)
{
    var providerFilter = command.GetOption("provider");
    foreach (var provider in ChatProviderCatalog.All.Where(provider =>
                 string.IsNullOrWhiteSpace(providerFilter) ||
                 string.Equals(provider.Id, providerFilter, StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine($"{provider.Id} - {provider.Name}");
        foreach (var model in provider.Models)
        {
            var profile = ModelProfileCatalog.Resolve(provider.Id, model.Id);
            Console.WriteLine($"  {model.Id} ({model.CapabilityLabel})");
            Console.WriteLine($"    profile: {profile.DisplayName}; default mode: {profile.DefaultExecutionMode}");
        }
    }
}

static void PrintActiveConfig(AppSettings settings)
{
    var active = ProviderSettingsService.GetSelectedProvider(settings);
    Console.WriteLine($"Active provider: {active?.Name ?? "(none)"}");
    Console.WriteLine($"Model: {active?.SelectedModelId ?? settings.Model}");
    Console.WriteLine($"Base URL: {active?.BaseUrl ?? settings.BaseUrl}");
    Console.WriteLine($"Execution mode: {settings.AgentExecutionMode}");
    Console.WriteLine($"Tool rounds: {settings.AgentMaxToolRounds}");
    Console.WriteLine($"Auto verify: {settings.AutoVerifyAgentRuns}");
}

static void PrintConfiguredProviders(AppSettings settings)
{
    if (settings.ConfiguredProviders.Count == 0)
    {
        Console.WriteLine("No configured providers.");
        return;
    }

    foreach (var provider in settings.ConfiguredProviders)
    {
        var active = provider.Id == settings.ActiveConfiguredProviderId ? "*" : " ";
        Console.WriteLine($"{active} {provider.Id}");
        Console.WriteLine($"  provider: {provider.Name} ({provider.TemplateId})");
        Console.WriteLine($"  model: {provider.SelectedModelId}");
        Console.WriteLine($"  base URL: {provider.BaseUrl}");
    }
}

static void PrintVersion()
{
    Console.WriteLine(GetVersion());
}

static string GetVersion()
{
    return typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
           typeof(Program).Assembly.GetName().Version?.ToString() ??
           "0.5.0";
}

static string ResolveProjectPath(string projectPath)
{
    var value = string.IsNullOrWhiteSpace(projectPath)
        ? Environment.CurrentDirectory
        : projectPath;
    return Path.GetFullPath(value);
}

static ProjectWorkspace? FindProject(IEnumerable<ProjectWorkspace> projects, string projectPath)
{
    var fullPath = Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return projects.Where(project => !string.IsNullOrWhiteSpace(project.Path)).FirstOrDefault(project =>
        string.Equals(
            Path.GetFullPath(project.Path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            fullPath,
            StringComparison.OrdinalIgnoreCase));
}

static string BuildProjectSnapshot(ProjectWorkspace project)
{
    var snapshot = ProjectLoadSnapshotBuilder.Build(project);
    return string.Join(Environment.NewLine, [
        snapshot.HealthText,
        snapshot.ProfileText,
        snapshot.ActivityText,
        snapshot.RecommendationText
    ]);
}

static void PrintContextReport(
    ProjectWorkspace project,
    ProjectFileIndex fileIndex,
    TaskContextPack contextPack,
    string goal,
    int maxTokens,
    int maxFiles)
{
    Console.WriteLine($"Project: {project.Name}");
    Console.WriteLine($"Path: {project.Path}");
    Console.WriteLine($"Goal: {goal}");
    Console.WriteLine($"Budget: {maxTokens} context tokens; index cap: {maxFiles} files");
    Console.WriteLine();
    Console.WriteLine(BuildProjectSnapshot(project));
    Console.WriteLine();

    Console.WriteLine($"File index: {fileIndex.Entries.Count} files");
    foreach (var group in fileIndex.Entries
                 .GroupBy(entry => string.IsNullOrWhiteSpace(entry.TypeTag) ? "other" : entry.TypeTag)
                 .OrderByDescending(group => group.Count())
                 .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                 .Take(8))
    {
        Console.WriteLine($"  {group.Key}: {group.Count()}");
    }

    Console.WriteLine();
    Console.WriteLine(contextPack.Summary);
    PrintFileRefs("Included files", contextPack.IncludedFiles, 12);
    PrintFileRefs("Omitted relevant files", contextPack.OmittedButRelevantRefs, 8);

    if (contextPack.IncludedSnippets.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Snippets:");
        foreach (var snippet in contextPack.IncludedSnippets.Take(8))
        {
            Console.WriteLine($"  - {snippet}");
        }
    }

    if (project.VerificationCommands.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Verification commands:");
        foreach (var command in project.VerificationCommands.Take(8))
        {
            Console.WriteLine($"  - {command.Name}: {command.Command}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Cache hints:");
    Console.WriteLine("  - Keep AGENTS.md, README, and stable config files small and stable.");
    Console.WriteLine("  - Pin recurring task files instead of asking the model to rediscover them.");
    Console.WriteLine("  - Use narrower goals and lower --tokens for fast/cache-friendly runs.");
}

static void PrintFileRefs(string title, IReadOnlyList<TaskContextFileRef> files, int limit)
{
    if (files.Count == 0)
    {
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"{title}:");
    foreach (var file in files.Take(limit))
    {
        Console.WriteLine($"  - {file.Path} ({file.TypeTag}, score {file.Score:0.##})");
        if (!string.IsNullOrWhiteSpace(file.Reason))
        {
            Console.WriteLine($"    {file.Reason}");
        }
    }
}

static int ParsePositiveInt(string value, int fallback)
{
    return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}

static void PrintHelp()
{
    Console.WriteLine("""
    AIChat.Cli

    Usage:
      aichat --version
      aichat models [--provider deepseek]
      aichat config show
      aichat config list
      aichat config use --provider deepseek [--model deepseek-chat]
      aichat config set-provider --provider deepseek --api-key <key> [--model deepseek-chat] [--base-url <url>]
      aichat projects list
      aichat context [goal] [--project <path>] [--tokens 1200] [--max-files 500]
      aichat doctor
      aichat init [--project <path>] [--name <name>]
      aichat ask "fix the failing test" [--project <path>] [--mode fast|standard|deep] [--plain] [--yes] [--no-write] [--verify]
      aichat tui [--project <path>] [--mode fast|standard|deep] [--plain] [--yes] [--no-write] [--verify]

    Global options:
      --data-dir <path>   Settings/projects directory. Defaults to the platform app data directory.
      -h, --help          Show help.
    """);
}

sealed record CliCommand(
    string Name,
    IReadOnlyList<string> Positionals,
    IReadOnlyDictionary<string, string> Options,
    IReadOnlySet<string> Flags,
    string DataDirectory,
    bool ShowHelp,
    bool HasError)
{
    public string GetOption(string name) => Options.TryGetValue(name, out var value) ? value : "";
    public bool HasFlag(string name) => Flags.Contains(name);

    public static CliCommand Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            return Empty(showHelp: true);
        }

        var name = args[0];
        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dataDirectory = "";

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--data-dir":
                    if (!ReadValue(args, ref i, arg, out dataDirectory))
                    {
                        return Empty(showHelp: true, hasError: true);
                    }

                    break;
                case "--provider":
                case "--api-key":
                case "--model":
                case "--base-url":
                case "--project":
                case "--name":
                case "--mode":
                case "--tokens":
                case "--max-files":
                    if (!ReadValue(args, ref i, arg, out var value))
                    {
                        return Empty(showHelp: true, hasError: true);
                    }

                    options[arg[2..]] = value;
                    break;
                case "--plain":
                case "--yes":
                case "--no-write":
                case "--verify":
                    flags.Add(arg[2..]);
                    break;
                default:
                    positionals.Add(arg);
                    break;
            }
        }

        return new CliCommand(name, positionals, options, flags, dataDirectory, false, false);
    }

    private static CliCommand Empty(bool showHelp, bool hasError = false) =>
        new("", [], new Dictionary<string, string>(), new HashSet<string>(), "", showHelp, hasError);

    private static bool ReadValue(string[] args, ref int index, string option, out string value)
    {
        value = "";
        if (++index >= args.Length)
        {
            Console.Error.WriteLine($"{option} requires a value.");
            return false;
        }

        value = args[index];
        return true;
    }
}
