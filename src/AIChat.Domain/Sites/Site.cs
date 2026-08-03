using System.Text.Json.Serialization;

namespace AIChat.Domain.Sites;

// Wave 9 (parity plan §7 Wave 9): one row in the
// "站点" (Sites) panel. A Site is a project the user
// wants to preview locally (and eventually deploy to
// cloud). The first slice ships the data model + the
// local-preview surface; cloud deploy is hidden behind
// a "无 Hosting Provider" hint because AIChat has no
// cloud integration in this scope (plan §5.4).
//
// Plan §7 Wave 9 calls out:
//   * local preview must really run (no fake iframe)
//   * project list / create / preview / save / deploy /
//     env var management
//   * env var management lives here
//   * deploy history persists across restart
//   * cloud deploy is hidden when no adapter is
//     available
public sealed class Site
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // The project (or folder under the project) the site
    // previews from. Empty string means "no project yet"
    // — the user is in the middle of creating.
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = "";

    // "auto" = let the runner detect vite / next / static
    // and pick the right preview command. Power-user
    // override is "custom" + a free-form command.
    [JsonPropertyName("previewMode")]
    public SitePreviewMode PreviewMode { get; set; } = SitePreviewMode.Auto;

    [JsonPropertyName("customCommand")]
    public string CustomCommand { get; set; } = "";

    // Local port for the preview server. 0 = "let the
    // runner pick a free port". The runner writes the
    // actual port back here when it allocates one so the
    // user can find their preview across restarts.
    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("envVars")]
    public List<SiteEnvVar> EnvVars { get; set; } = [];

    // Last deployment adapter name. "local" for the
    // built-in local preview, or a hosting provider id
    // (none in this slice). The history list rolls up
    // every successful deploy under the same adapter.
    [JsonPropertyName("adapterId")]
    public string AdapterId { get; set; } = "local";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("lastPreviewAt")]
    public DateTimeOffset? LastPreviewAt { get; set; }
}

public enum SitePreviewMode
{
    Auto = 0,
    Custom = 1,
}

public sealed class SiteEnvVar
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    // Persisted in the local sites.json (NOT in the
    // keychain). Plan §7 Wave 9 accepts this for the
    // first slice because Sites env vars are typically
    // PUBLIC config (NEXT_PUBLIC_*, VITE_*, etc.). A
    // follow-up slice adds a "secret" mode that routes
    // through OS keychain.
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

// One row in the Site's deployment history. Persists
// across restarts so a user returning to the app can see
// "this site last deployed at X, status Y". The first
// slice records local-preview runs (which is the only
// "deployment" we have). Cloud deploy rows will plug in
// via the same record shape.
public sealed class SiteDeployment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("siteId")]
    public string SiteId { get; set; } = "";

    [JsonPropertyName("adapterId")]
    public string AdapterId { get; set; } = "local";

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("status")]
    public SiteDeploymentStatus Status { get; set; } = SiteDeploymentStatus.Running;

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }
}

public enum SiteDeploymentStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Stopped = 3,
}
