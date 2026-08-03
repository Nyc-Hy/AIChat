namespace AIChat.Storage.Json;

// Read-time override that lets dev / CI / shell users skip the platform
// credential vault entirely by exporting an API key in the environment. When
// active, the override is the source of truth: settings.json's stored
// `protectedApiKey` field is left untouched, and saves do not write back to
// the vault. Daily-driver use: `export AICHAT_API_KEY=sk-...` in shell rc
// once, never see a keychain prompt again.
//
// Precedence (per purpose):
//   1. Provider-specific env var: `AICHAT_PROVIDER_<NAME>_API_KEY`
//      (`<NAME>` is `ConfiguredLlmProvider.Name` uppercased, non-alphanum → `_`).
//   2. Catch-all: `AICHAT_API_KEY` covers both main key and any provider
//      that does not have its own override.
internal static class EnvironmentSecretOverride
{
    internal const string MainKeyEnvVar = "AICHAT_API_KEY";
    internal const string ProviderKeyEnvVarPrefix = "AICHAT_PROVIDER_";
    internal const string ProviderKeyEnvVarSuffix = "_API_KEY";
    internal const string MainKeyPurpose = "settings-api-key";

    public static bool IsActive =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MainKeyEnvVar));

    public static bool TryGetMainKey(out string value)
        => TryReadEnv(MainKeyEnvVar, out value);

    public static bool TryGetProviderKey(string providerName, out string value)
    {
        var specific = ProviderKeyEnvVarPrefix + NormalizeProviderName(providerName) + ProviderKeyEnvVarSuffix;
        if (TryReadEnv(specific, out value))
        {
            return true;
        }

        return TryReadEnv(MainKeyEnvVar, out value);
    }

    public static string ProviderKeyPurpose(string providerId)
        => $"provider-{providerId}-api-key";

    private static bool TryReadEnv(string variable, out string value)
    {
        var raw = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = "";
            return false;
        }

        value = raw.Trim();
        return true;
    }

    internal static string NormalizeProviderName(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return "";
        }

        var buffer = new System.Text.StringBuilder(providerName.Length);
        foreach (var ch in providerName)
        {
            buffer.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_');
        }
        return buffer.ToString();
    }
}
