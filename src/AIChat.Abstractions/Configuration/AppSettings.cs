namespace AIChat.Abstractions.Configuration;

using AIChat.Abstractions.Llm;
using System.Text.Json.Serialization;

// Persisted application settings plus the currently selected provider/model.
// It lives in Abstractions because UI, storage, and providers all need this shape.
public sealed class AppSettings
{
    // Monotonic local-storage revision. Repositories reject stale whole-document
    // saves instead of silently overwriting settings written by another window.
    public long PersistenceRevision { get; set; }
    public string ProviderId { get; set; } = "minimax";
    public string ProtocolId { get; set; } = "openai";
    public string ProviderName { get; set; } = "MiniMax";
    public string BaseUrl { get; set; } = "https://api.minimax.io/v1";
    [JsonIgnore]
    public string ApiKey { get; set; } = "";
    public string ProtectedApiKey { get; set; } = "";
    public string ApiKeyProtection { get; set; } = "";
    public string Model { get; set; } = "MiniMax-M3";
    // Code-agent workflows benefit from stable, low-variance behavior.
    public double Temperature { get; set; } = 0.3;
    public int ModelContextLimit { get; set; } = 1_000_000;
    public bool ModelSupportsVision { get; set; }
    public Dictionary<string, string> ModelParameters { get; set; } = [];
    public string ActiveConfiguredProviderId { get; set; } = "";
    // Restore last active project and conversation on next launch.
    public string LastActiveProjectId { get; set; } = "";
    public string LastActiveConversationId { get; set; } = "";
    // Tool IDs selected in Settings. Only these schemas are sent to the model.
    public List<string> EnabledToolIds { get; set; } = [];
    public Dictionary<string, ToolPermissionMode> ToolPermissionModes { get; set; } = [];
    public int AgentMaxToolRounds { get; set; } = 16;
    public AgentExecutionMode AgentExecutionMode { get; set; } = AgentExecutionMode.Standard;
    // Maximum output tokens for LLM responses. Providers that support this
    // parameter will use it; others ignore it.
    public int MaxOutputTokens { get; set; } = 4096;
    public bool AutoVerifyAgentRuns { get; set; }
    public int MaxAutoFixRounds { get; set; }
    public bool AgentAdaptiveStrategiesEnabled { get; set; }
    public bool AgentAdaptiveBudgetAndExplorerEnabled { get; set; }
    // Multiple configured providers lets the user keep more than one API key or
    // model setup while the rest of the app only reads the active one.
    public List<ConfiguredLlmProvider> ConfiguredProviders { get; set; } = [];

    // --- Enterprise settings ---

    // Number of retry attempts for transient LLM errors (429, 500, etc.)
    public int RetryMaxAttempts { get; set; } = 3;
    // Fraction of model context limit reserved for conversation (0.0-1.0).
    // The remainder is left for agent tools and system prompt.
    public double ConversationContextRatio { get; set; } = 0.7;
    // Use tokenizer-based context estimation (SharpToken) vs character heuristic.
    public bool UseTokenizerEstimation { get; set; } = true;
    // Maximum audit log file size in bytes before rotation (default 5MB).
    public long AuditLogMaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;
    // Number of days to retain rotated audit log archives.
    public int AuditLogRetentionDays { get; set; } = 30;

    // Visual theme preference. Persisted so the user's choice survives
    // restarts. PR-9 added this; older settings files deserialise as
    // the default (System) which lets the platform decide.
    public ThemePreference ThemePreference { get; set; } = ThemePreference.System;

    // ---- Sprint 0.5: 2-toggle permission model (Codex parity) ----
    // Two independent toggles that compose into 4 effective states:
    //   - both off         → "read only" (no writes, no network beyond workspace reads)
    //   - default on only  → "default access" (workspace writes, prompt for network)
    //   - both on          → "full access" (writes, network, no approvals)
    // Matches Codex Desktop's two-toggles-in-Settings layout. See
    // CODEX_DESKTOP_PARITY_PLAN.md §13.5 deviation #1.
    public bool DefaultAccess { get; set; } = true;
    public bool FullAccessEnabled { get; set; }

    // Sprint 0.5: Environment panel right-column visibility. Persisted
    // across launches so the user's preferred layout survives restart.
    public bool EnvironmentPanelOpen { get; set; } = true;

    // 2026-08-03: main window position + size + maximised state.
    // Persisted across launches so a user with a multi-monitor
    // setup, a 4K scaling preference, or a non-default layout keeps
    // that layout after a restart. The defaults are deliberately
    // numeric (not NaN) so the AppSettings JSON serialiser does
    // not need a custom number-handling option to round-trip them.
    // A 0 / 0 origin means "not yet positioned" — the host falls
    // back to the platform default in that case.
    public double WindowX { get; set; }
    public double WindowY { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}
