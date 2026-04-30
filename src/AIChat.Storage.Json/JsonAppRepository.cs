using System.Text.Json;
using AIChat.Domain.Chat;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Persistence;
using AIChat.Domain.Projects;

namespace AIChat.Storage.Json;

// Local JSON implementation of IAppRepository. It keeps the MVP transparent:
// settings and conversations can be inspected under %APPDATA%\AIChat.
public sealed class JsonAppRepository : IAppRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _dataDirectory;
    private readonly string _settingsPath;
    private readonly string _projectsPath;

    public JsonAppRepository()
    {
        // User-specific app data avoids writing into the source tree and survives
        // app restarts without requiring a database.
        _dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AIChat");
        _settingsPath = Path.Combine(_dataDirectory, "settings.json");
        _projectsPath = Path.Combine(_dataDirectory, "projects.json");
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return CreateInitialSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
               ?? CreateInitialSettings();
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_dataDirectory);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
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
        Directory.CreateDirectory(_dataDirectory);
        await using var stream = File.Create(_projectsPath);
        await JsonSerializer.SerializeAsync(stream, projects, JsonOptions, cancellationToken);
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
            ModelContextLimit = 1_000_000
        };
    }

    private static List<ProjectWorkspace> CreateInitialProjects()
    {
        var project = new ProjectWorkspace
        {
            Name = "AIChat",
            Path = Environment.CurrentDirectory,
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
