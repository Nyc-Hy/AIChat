using AIChat.Abstractions.Configuration;

namespace AIChat.Storage.Json;

// Read-time override that lets dev / CI / daily-driver users skip the
// platform credential vault entirely. Three sources are consulted in
// priority order; the first hit wins. When active, the override is the
// source of truth: settings.json's stored `protectedApiKey` field is
// left untouched, and saves do not write back to the vault.
//
// Sources (highest priority first):
//   1. Environment variables (`AICHAT_API_KEY`,
//      `AICHAT_PROVIDER_<NAME>_API_KEY`).
//   2. The file pointed to by `AICHAT_API_KEY_FILE`. Each line is
//      either a bare secret (used as the main key) or `KEY=VALUE` in
//      dotenv style (used as a per-purpose override; keys match the
//      same naming as the env-var layer).
//   3. `<dataDir>/.env` — same dotenv format as #2. The data
//      directory is platform-conventional
//      (macOS `~/Library/Application Support/AIChat/` etc.), so the
//      file is reached by every launcher (Finder / Dock / Spotlight /
//      shell-launched `dotnet run`) without depending on the shell
//      environment being initialised.
//
// Why file-based: macOS GUI apps launched from Finder / Dock /
// Spotlight do NOT inherit the user's shell rc. An env var that works
// in `dotnet run` is silently absent when the same binary is launched
// from the GUI, which is how daily-driver users actually open the
// app. A file on disk does not have that problem.
internal static class EnvironmentSecretOverride
{
    internal const string MainKeyEnvVar = "AICHAT_API_KEY";
    internal const string ProviderKeyEnvVarPrefix = "AICHAT_PROVIDER_";
    internal const string ProviderKeyEnvVarSuffix = "_API_KEY";
    internal const string MainKeyPurpose = "settings-api-key";
    internal const string ApiKeyFileEnvVar = "AICHAT_API_KEY_FILE";
    internal const string DefaultDotEnvFileName = ".env";

    // Cached dotenv parse of the data-directory file. Keyed by
    // variable name; a missing / unreadable file produces an empty
    // cache and is treated as "not active" for that source.
    private static System.Collections.Generic.Dictionary<string, string>? _dotEnvCache;
    private static string? _dotEnvCachePath;

    public static bool IsActive =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MainKeyEnvVar))
        || TryGetDotEnv(MainKeyEnvVar, out _)
        || TryGetApiKeyFileContents(out _);

    public static bool TryGetMainKey(out string value)
        => TryGetFromAnySource(MainKeyEnvVar, out value);

    public static bool TryGetProviderKey(string providerName, out string value)
    {
        var specific = ProviderKeyEnvVarPrefix + NormalizeProviderName(providerName) + ProviderKeyEnvVarSuffix;
        if (TryGetFromAnySource(specific, out value))
        {
            return true;
        }

        return TryGetFromAnySource(MainKeyEnvVar, out value);
    }

    public static string ProviderKeyPurpose(string providerId)
        => $"provider-{providerId}-api-key";

    private static bool TryGetFromAnySource(string variable, out string value)
    {
        // 1. Direct env var (works for shell-launched processes).
        if (TryReadEnv(variable, out value))
        {
            return true;
        }

        // 2. AICHAT_API_KEY_FILE points at a single-purpose secret
        //    file; the contents are the main key, not dotenv. Only
        //    consulted for the main-key lookup.
        if (string.Equals(variable, MainKeyEnvVar, StringComparison.Ordinal))
        {
            if (TryGetApiKeyFileContents(out value))
            {
                return true;
            }
        }

        // 3. dotenv file under the data directory.
        if (TryGetDotEnv(variable, out value))
        {
            return true;
        }

        value = "";
        return false;
    }

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

    private static bool TryGetApiKeyFileContents(out string value)
    {
        value = "";
        var path = Environment.GetEnvironmentVariable(ApiKeyFileEnvVar);
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }
            var raw = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }
            value = raw;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDotEnv(string variable, out string value)
    {
        value = "";
        try
        {
            var path = GetDotEnvPath();
            if (!File.Exists(path))
            {
                return false;
            }

            if (!string.Equals(_dotEnvCachePath, path, StringComparison.Ordinal))
            {
                _dotEnvCache = ParseDotEnv(path);
                _dotEnvCachePath = path;
            }

            if (_dotEnvCache is null)
            {
                return false;
            }

            return _dotEnvCache.TryGetValue(variable, out value!) && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    // Minimal dotenv parser: each non-empty, non-comment line is
    // either `KEY=VALUE` (the `=` splits the pair, surrounding
    // whitespace is trimmed, surrounding double-quotes are stripped
    // from VALUE) or a bare line treated as the main key. Comments
    // start with `#` and may appear at the start of a line. The
    // parser is deliberately tiny — no escaping, no multi-line
    // values, no `${VAR}` expansion — because the only consumer is
    // our own loader and the file format is only intended to be
    // hand-edited by the user.
    private static System.Collections.Generic.Dictionary<string, string> ParseDotEnv(string path)
    {
        var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var eq = line.IndexOf('=');
            string key;
            string val;
            if (eq < 0)
            {
                // Bare line: treat the whole line as the main key.
                key = MainKeyEnvVar;
                val = line;
            }
            else
            {
                key = line.Substring(0, eq).Trim();
                val = line.Substring(eq + 1).Trim();
                if (val.Length >= 2 && val[0] == '"' && val[val.Length - 1] == '"')
                {
                    val = val.Substring(1, val.Length - 2);
                }
            }

            if (key.Length == 0 || val.Length == 0)
            {
                continue;
            }

            map[key] = val;
        }
        return map;
    }

    // Exposed for tests; not part of the public contract.
    internal static System.Collections.Generic.IReadOnlyDictionary<string, string>? CachedDotEnvForTest
    {
        get
        {
            try
            {
                var path = GetDotEnvPath();
                if (!File.Exists(path))
                {
                    return null;
                }
                if (!string.Equals(_dotEnvCachePath, path, StringComparison.Ordinal))
                {
                    _dotEnvCache = ParseDotEnv(path);
                    _dotEnvCachePath = path;
                }
                return _dotEnvCache;
            }
            catch
            {
                return null;
            }
        }
    }

    // Test-only escape hatch: forces the next dotenv lookup to read
    // from a specific path. The cache is invalidated immediately so
    // the test does not race against a previous parse. Production
    // callers never invoke this — they go through TryGetDotEnv which
    // resolves the data-directory path itself.
    internal static void SetDotEnvPathForTest(string path)
    {
        _dotEnvCachePath = path;
        _dotEnvCache = null;
    }

    private static string GetDotEnvPath()
        => Path.Combine(AppRuntimeProfile.DataDirectory, DefaultDotEnvFileName);

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
