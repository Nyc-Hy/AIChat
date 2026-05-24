using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.Domain.Projects;

namespace AIChat.Storage.Json;

// Local JSON implementation of IAppRepository. Settings and conversations are
// stored under %APPDATA%\AIChat with atomic-write semantics to prevent data
// corruption on concurrent or interrupted saves.
public sealed class JsonAppRepository : IAppRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
    };

    private readonly string _dataDirectory;
    private readonly string _settingsPath;
    private readonly string _projectsPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonAppRepository()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIChat"))
    {
    }

    public JsonAppRepository(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _projectsPath = Path.Combine(_dataDirectory, "projects.json");
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return CreateInitialSettings();
        }

        var json = await File.ReadAllTextAsync(_settingsPath, cancellationToken);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                       ?? CreateInitialSettings();
        RestoreLegacyPlainTextApiKeys(settings, json);
        return ProtectedSettingsSerializer.RestoreAfterLoad(settings);
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await AtomicWriteJsonAsync(_settingsPath, ProtectedSettingsSerializer.PrepareForSave(settings), cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectWorkspace>> LoadProjectsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_projectsPath))
        {
            return CreateInitialProjects();
        }

        await using var stream = File.OpenRead(_projectsPath);
        var projects = await JsonSerializer.DeserializeAsync<List<ProjectWorkspace>>(stream, JsonOptions, cancellationToken);
        // Empty or old demo-seeded data is treated as first launch so the user
        // starts from a clean workspace.
        return projects is null || projects.Count == 0 || IsLegacySeedData(projects)
            ? CreateInitialProjects()
            : projects;
    }

    public async Task SaveProjectsAsync(IReadOnlyList<ProjectWorkspace> projects, CancellationToken cancellationToken = default)
    {
        await AtomicWriteJsonAsync(_projectsPath, projects, cancellationToken);
    }

    private async Task AtomicWriteJsonAsync<T>(string filePath, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_dataDirectory);
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.Create(tempPath);
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Close();
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
            // Clean up temp file if rename failed
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    private static AppSettings CreateInitialSettings()
    {
        return new AppSettings
        {
            ProviderId = "tokenplan-mimo",
            ProtocolId = "openai",
            ProviderName = "小米 MIMO (TokenPlan)",
            BaseUrl = "https://token-plan-cn.xiaomimimo.com/v1",
            ApiKey = "",
            Model = "mimo-v2.5-pro",
            ModelContextLimit = 1_000_000,
            AgentMaxToolRounds = 16,
            AgentExecutionMode = AgentExecutionMode.Standard,
            MaxAutoFixRounds = 0,
            AutoVerifyAgentRuns = false,
            AgentAdaptiveStrategiesEnabled = false,
            AgentAdaptiveBudgetAndExplorerEnabled = false,
            AgentAdaptiveRecoveryEnabled = false,
            AgentAdaptiveAutoVerifyEnabled = false
        };
    }

    private static void RestoreLegacyPlainTextApiKeys(AppSettings settings, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("apiKey", out var apiKeyElement))
            {
                settings.ApiKey = apiKeyElement.GetString() ?? "";
            }

            if (!root.TryGetProperty("configuredProviders", out var providersElement) ||
                providersElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var index = 0;
            foreach (var providerElement in providersElement.EnumerateArray())
            {
                if (index >= settings.ConfiguredProviders.Count)
                {
                    break;
                }

                if (providerElement.TryGetProperty("apiKey", out var providerApiKeyElement))
                {
                    settings.ConfiguredProviders[index].ApiKey = providerApiKeyElement.GetString() ?? "";
                }

                index++;
            }
        }
        catch (JsonException)
        {
            // Invalid JSON will be handled by the normal deserialize path.
        }
    }

    private static List<ProjectWorkspace> CreateInitialProjects()
    {
        var project = new ProjectWorkspace
        {
            Name = "AIChat",
            Path = "",
            UpdatedAt = DateTimeOffset.Now
        };

        return [project];
    }

    private static bool IsLegacySeedData(IReadOnlyList<ProjectWorkspace> projects)
    {
        return projects.Any(project =>
            project.Conversations.Any(conversation =>
                conversation.Messages.Any(message =>
                    message.Content.Contains("第一版先做项目级对话", StringComparison.Ordinal))));
    }
}
