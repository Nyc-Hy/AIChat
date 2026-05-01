namespace AIChat.Abstractions.Configuration;

using AIChat.Abstractions.Llm;

// Persisted application settings plus the currently selected provider/model.
// It lives in Abstractions because UI, storage, and providers all need this shape.
public sealed class AppSettings
{
    public string ProviderId { get; set; } = "tokenplan-mimo";
    public string ProtocolId { get; set; } = "openai";
    public string ProviderName { get; set; } = "小米 MIMO (TokenPlan)";
    public string BaseUrl { get; set; } = "https://token-plan-cn.xiaomimimo.com/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "mimo-v2.5-pro";
    // Code-agent workflows benefit from stable, low-variance behavior.
    public double Temperature { get; set; } = 0.3;
    public int ModelContextLimit { get; set; } = 1_000_000;
    public Dictionary<string, string> ModelParameters { get; set; } = [];
    public string ActiveConfiguredProviderId { get; set; } = "";
    // Tool IDs selected in Settings. Only these schemas are sent to the model.
    public List<string> EnabledToolIds { get; set; } = [];
    public Dictionary<string, ToolPermissionMode> ToolPermissionModes { get; set; } = [];
    public int AgentMaxToolRounds { get; set; } = 4;
    public bool AutoVerifyAgentRuns { get; set; }
    public int MaxAutoFixRounds { get; set; } = 3;
    // Multiple configured providers lets the user keep more than one API key or
    // model setup while the rest of the app only reads the active one.
    public List<ConfiguredLlmProvider> ConfiguredProviders { get; set; } = [];
}
