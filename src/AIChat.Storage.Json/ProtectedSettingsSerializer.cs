using System.Runtime.InteropServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;

namespace AIChat.Storage.Json;

internal sealed record CachedProtectedSecret(
    string Secret,
    string ProtectedValue,
    string Protection);

internal sealed record RestoredSettingsSecrets(
    AppSettings Settings,
    Dictionary<string, CachedProtectedSecret> Cache);

internal sealed record PreparedSettingsSave(
    AppSettings PersistedSettings,
    Dictionary<string, CachedProtectedSecret> Cache,
    IReadOnlyList<string> DeletePurposes);

internal static class ProtectedSettingsSerializer
{
    private const string DpapiCurrentUser = "dpapi-current-user";
    private const string LegacyPlain = "plain";

    public static PreparedSettingsSave PrepareForSave(
        AppSettings settings,
        ISecretProtector secretProtector,
        IReadOnlyDictionary<string, CachedProtectedSecret>? cachedSecrets = null,
        AppSettings? persistedSecretMetadata = null,
        bool persistSecretChanges = true,
        bool forceProtect = false)
    {
        var previous = cachedSecrets ?? new Dictionary<string, CachedProtectedSecret>(StringComparer.Ordinal);
        var next = new Dictionary<string, CachedProtectedSecret>(previous, StringComparer.Ordinal);
        var deletePurposes = new HashSet<string>(StringComparer.Ordinal);
        var currentPurposes = new HashSet<string>(StringComparer.Ordinal);
        var copy = Clone(settings);

        const string settingsPurpose = "settings-api-key";
        currentPurposes.Add(settingsPurpose);
        var settingsProtectedValue = !persistSecretChanges && persistedSecretMetadata is not null
            ? persistedSecretMetadata.ProtectedApiKey
            : copy.ProtectedApiKey;
        var settingsProtection = !persistSecretChanges && persistedSecretMetadata is not null
            ? persistedSecretMetadata.ApiKeyProtection
            : copy.ApiKeyProtection;
        var protectedSecret = PrepareSecret(
            copy.ApiKey,
            settingsProtectedValue,
            settingsProtection,
            settingsPurpose,
            secretProtector,
            previous,
            next,
            deletePurposes,
            persistSecretChanges,
            forceProtect);
        copy.ProtectedApiKey = protectedSecret.ProtectedValue;
        copy.ApiKeyProtection = protectedSecret.Protection;
        copy.ApiKey = "";

        foreach (var provider in copy.ConfiguredProviders)
        {
            var purpose = ProviderPurpose(provider.Id);
            currentPurposes.Add(purpose);
            var persistedProvider = !persistSecretChanges
                ? persistedSecretMetadata?.ConfiguredProviders.FirstOrDefault(item => item.Id == provider.Id)
                : null;
            var providerProtectedValue = !persistSecretChanges && persistedSecretMetadata is not null
                ? persistedProvider?.ProtectedApiKey ?? ""
                : provider.ProtectedApiKey;
            var providerProtection = !persistSecretChanges && persistedSecretMetadata is not null
                ? persistedProvider?.ApiKeyProtection ?? ""
                : provider.ApiKeyProtection;
            protectedSecret = PrepareSecret(
                provider.ApiKey,
                providerProtectedValue,
                providerProtection,
                purpose,
                secretProtector,
                previous,
                next,
                deletePurposes,
                persistSecretChanges,
                forceProtect);
            provider.ProtectedApiKey = protectedSecret.ProtectedValue;
            provider.ApiKeyProtection = protectedSecret.Protection;
            provider.ApiKey = "";
        }

        if (persistSecretChanges)
        {
            foreach (var stalePurpose in previous.Keys.Where(purpose => !currentPurposes.Contains(purpose)))
            {
                if (ShouldDeleteFromProtector(previous[stalePurpose].Protection))
                {
                    deletePurposes.Add(stalePurpose);
                }
                next.Remove(stalePurpose);
            }
        }

        return new PreparedSettingsSave(copy, next, deletePurposes.ToList());
    }

    public static RestoredSettingsSecrets RestoreAfterLoad(
        AppSettings settings,
        ISecretProtector secretProtector,
        IReadOnlyDictionary<string, CachedProtectedSecret>? cachedSecrets = null)
    {
        var next = cachedSecrets is null
            ? new Dictionary<string, CachedProtectedSecret>(StringComparer.Ordinal)
            : new Dictionary<string, CachedProtectedSecret>(cachedSecrets, StringComparer.Ordinal);
        settings.ApiKey = RestoreSecret(
            settings.ApiKey,
            settings.ProtectedApiKey,
            settings.ApiKeyProtection,
            "settings-api-key",
            secretProtector,
            next);

        foreach (var provider in settings.ConfiguredProviders)
        {
            provider.ApiKey = RestoreSecret(
                provider.ApiKey,
                provider.ProtectedApiKey,
                provider.ApiKeyProtection,
                ProviderPurpose(provider.Id),
                secretProtector,
                next);
        }

        return new RestoredSettingsSecrets(settings, next);
    }

    public static void ApplyProtectionMetadata(AppSettings settings, AppSettings persistedSettings)
    {
        settings.ProtectedApiKey = persistedSettings.ProtectedApiKey;
        settings.ApiKeyProtection = persistedSettings.ApiKeyProtection;
        foreach (var protectedProvider in persistedSettings.ConfiguredProviders)
        {
            var liveProvider = settings.ConfiguredProviders.FirstOrDefault(item => item.Id == protectedProvider.Id);
            if (liveProvider is not null)
            {
                liveProvider.ProtectedApiKey = protectedProvider.ProtectedApiKey;
                liveProvider.ApiKeyProtection = protectedProvider.ApiKeyProtection;
            }
        }
    }

    private static CachedProtectedSecret PrepareSecret(
        string secret,
        string protectedValue,
        string protection,
        string cacheKey,
        ISecretProtector secretProtector,
        IReadOnlyDictionary<string, CachedProtectedSecret> previous,
        IDictionary<string, CachedProtectedSecret> next,
        ISet<string> deletePurposes,
        bool persistSecretChanges,
        bool forceProtect)
    {
        if (!persistSecretChanges)
        {
            if (previous.TryGetValue(cacheKey, out var cached) &&
                string.Equals(cached.ProtectedValue, protectedValue, StringComparison.Ordinal) &&
                string.Equals(cached.Protection, protection, StringComparison.OrdinalIgnoreCase))
            {
                next[cacheKey] = cached;
                return cached;
            }

            next.Remove(cacheKey);
            if (!string.IsNullOrWhiteSpace(protection))
            {
                return new CachedProtectedSecret("", protectedValue, protection);
            }

            return new CachedProtectedSecret("", "", "");
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            var previousProtection = previous.TryGetValue(cacheKey, out var cached)
                ? cached.Protection
                : protection;
            if (ShouldDeleteFromProtector(previousProtection))
            {
                deletePurposes.Add(cacheKey);
            }
            next.Remove(cacheKey);
            return new CachedProtectedSecret("", "", "");
        }

        if (!forceProtect &&
            previous.TryGetValue(cacheKey, out var existing) &&
            string.Equals(existing.Secret, secret, StringComparison.Ordinal) &&
            !string.Equals(existing.Protection, LegacyPlain, StringComparison.OrdinalIgnoreCase))
        {
            next[cacheKey] = existing;
            return existing;
        }

        if (OperatingSystem.IsWindows())
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
            var protectedSecret = new CachedProtectedSecret(
                secret,
                Convert.ToBase64String(WindowsDpapi.Protect(bytes)),
                DpapiCurrentUser);
            next[cacheKey] = protectedSecret;
            return protectedSecret;
        }

        var protectedResult = secretProtector.Protect(secret, cacheKey);
        var result = new CachedProtectedSecret(secret, protectedResult.Value, protectedResult.Protection);
        next[cacheKey] = result;
        return result;
    }

    private static string RestoreSecret(
        string currentSecret,
        string protectedValue,
        string protection,
        string purpose,
        ISecretProtector secretProtector,
        IDictionary<string, CachedProtectedSecret> cache)
    {
        if (cache.TryGetValue(purpose, out var cached) &&
            string.Equals(cached.ProtectedValue, protectedValue, StringComparison.Ordinal) &&
            string.Equals(cached.Protection, protection, StringComparison.OrdinalIgnoreCase))
        {
            return cached.Secret;
        }

        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return currentSecret;
        }

        var secret = UnprotectSecret(protectedValue, protection, purpose, secretProtector);
        cache[purpose] = new CachedProtectedSecret(secret, protectedValue, protection);
        return string.IsNullOrEmpty(secret) ? currentSecret : secret;
    }

    private static bool ShouldDeleteFromProtector(string protection)
    {
        return !string.IsNullOrWhiteSpace(protection) &&
               !string.Equals(protection, DpapiCurrentUser, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(protection, PlatformSecretProtector.SessionOnly, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(protection, LegacyPlain, StringComparison.OrdinalIgnoreCase);
    }

    private static string UnprotectSecret(
        string protectedValue,
        string protection,
        string purpose,
        ISecretProtector secretProtector)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return "";
        }

        try
        {
            if (string.Equals(protection, DpapiCurrentUser, StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows())
            {
                var bytes = Convert.FromBase64String(protectedValue);
                return System.Text.Encoding.UTF8.GetString(WindowsDpapi.Unprotect(bytes));
            }

            if (string.Equals(protection, LegacyPlain, StringComparison.OrdinalIgnoreCase))
            {
                // One-time migration path. JsonAppRepository immediately
                // rewrites legacy plaintext through the platform vault.
                return protectedValue;
            }

            return secretProtector.Unprotect(protectedValue, protection, purpose);
        }
        catch (Exception) when (
            OperatingSystem.IsWindows() ||
            string.Equals(protection, DpapiCurrentUser, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }
    }

    private static AppSettings Clone(AppSettings settings)
    {
        return new AppSettings
        {
            PersistenceRevision = settings.PersistenceRevision,
            ProviderId = settings.ProviderId,
            ProtocolId = settings.ProtocolId,
            ProviderName = settings.ProviderName,
            BaseUrl = settings.BaseUrl,
            ApiKey = settings.ApiKey,
            ProtectedApiKey = settings.ProtectedApiKey,
            ApiKeyProtection = settings.ApiKeyProtection,
            Model = settings.Model,
            Temperature = settings.Temperature,
            ModelContextLimit = settings.ModelContextLimit,
            ModelSupportsVision = settings.ModelSupportsVision,
            ModelParameters = new Dictionary<string, string>(settings.ModelParameters, StringComparer.OrdinalIgnoreCase),
            ActiveConfiguredProviderId = settings.ActiveConfiguredProviderId,
            LastActiveProjectId = settings.LastActiveProjectId,
            LastActiveConversationId = settings.LastActiveConversationId,
            EnabledToolIds = settings.EnabledToolIds.ToList(),
            ToolPermissionModes = new Dictionary<string, ToolPermissionMode>(settings.ToolPermissionModes, StringComparer.OrdinalIgnoreCase),
            AgentMaxToolRounds = settings.AgentMaxToolRounds,
            AgentExecutionMode = settings.AgentExecutionMode,
            MaxOutputTokens = settings.MaxOutputTokens,
            AutoVerifyAgentRuns = settings.AutoVerifyAgentRuns,
            MaxAutoFixRounds = settings.MaxAutoFixRounds,
            AgentAdaptiveStrategiesEnabled = settings.AgentAdaptiveStrategiesEnabled,
            AgentAdaptiveBudgetAndExplorerEnabled = settings.AgentAdaptiveBudgetAndExplorerEnabled,
            ConfiguredProviders = settings.ConfiguredProviders.Select(CloneProvider).ToList(),
            RetryMaxAttempts = settings.RetryMaxAttempts,
            ConversationContextRatio = settings.ConversationContextRatio,
            UseTokenizerEstimation = settings.UseTokenizerEstimation,
            AuditLogMaxFileSizeBytes = settings.AuditLogMaxFileSizeBytes,
            AuditLogRetentionDays = settings.AuditLogRetentionDays,
            ThemePreference = settings.ThemePreference,
            // Sprint 0.5: 2-toggle permission model + Environment panel state.
            DefaultAccess = settings.DefaultAccess,
            FullAccessEnabled = settings.FullAccessEnabled,
            EnvironmentPanelOpen = settings.EnvironmentPanelOpen,
            // 2026-08-03: window position / size / maximised state.
            WindowX = settings.WindowX,
            WindowY = settings.WindowY,
            WindowWidth = settings.WindowWidth,
            WindowHeight = settings.WindowHeight,
            WindowMaximized = settings.WindowMaximized
        };
    }

    private static string ProviderPurpose(string providerId)
        => $"provider-{providerId}-api-key";

    private static ConfiguredLlmProvider CloneProvider(ConfiguredLlmProvider provider)
    {
        return new ConfiguredLlmProvider
        {
            Id = provider.Id,
            TemplateId = provider.TemplateId,
            ProtocolId = provider.ProtocolId,
            Name = provider.Name,
            BaseUrl = provider.BaseUrl,
            ApiKey = provider.ApiKey,
            SelectedModelId = provider.SelectedModelId,
            SupportsVisionOverride = provider.SupportsVisionOverride,
            ModelParameters = new Dictionary<string, string>(provider.ModelParameters, StringComparer.OrdinalIgnoreCase),
            ProtectedApiKey = provider.ProtectedApiKey,
            ApiKeyProtection = provider.ApiKeyProtection
        };
    }

    private static class WindowsDpapi
    {
        private const int CryptProtectUiForbidden = 0x1;

        public static byte[] Protect(byte[] data)
        {
            return Crypt(data, protect: true);
        }

        public static byte[] Unprotect(byte[] data)
        {
            return Crypt(data, protect: false);
        }

        private static byte[] Crypt(byte[] data, bool protect)
        {
            var input = ToBlob(data);
            var output = new DataBlob();
            try
            {
                var success = protect
                    ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output);
                if (!success)
                {
                    throw new InvalidOperationException($"DPAPI operation failed with error {Marshal.GetLastWin32Error()}.");
                }

                var result = new byte[output.Count];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                if (input.Data != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(input.Data);
                }

                if (output.Data != IntPtr.Zero)
                {
                    LocalFree(output.Data);
                }
            }
        }

        private static DataBlob ToBlob(byte[] data)
        {
            var blob = new DataBlob
            {
                Count = data.Length,
                Data = Marshal.AllocHGlobal(data.Length)
            };
            Marshal.Copy(data, 0, blob.Data, data.Length);
            return blob;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string? dataDescription,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            int flags,
            ref DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr dataDescription,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr promptStruct,
            int flags,
            ref DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Count;
            public IntPtr Data;
        }
    }
}
