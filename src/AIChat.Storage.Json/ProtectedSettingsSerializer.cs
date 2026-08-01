using System.Runtime.InteropServices;
using AIChat.Abstractions.Configuration;
using AIChat.Abstractions.Llm;

namespace AIChat.Storage.Json;

internal static class ProtectedSettingsSerializer
{
    private const string DpapiCurrentUser = "dpapi-current-user";

    public static AppSettings PrepareForSave(AppSettings settings)
    {
        var copy = Clone(settings);
        ProtectSecret(copy.ApiKey, out var protectedApiKey, out var protection);
        copy.ProtectedApiKey = protectedApiKey;
        copy.ApiKeyProtection = protection;
        copy.ApiKey = "";

        foreach (var provider in copy.ConfiguredProviders)
        {
            ProtectSecret(provider.ApiKey, out protectedApiKey, out protection);
            provider.ProtectedApiKey = protectedApiKey;
            provider.ApiKeyProtection = protection;
            provider.ApiKey = "";
        }

        return copy;
    }

    public static AppSettings RestoreAfterLoad(AppSettings settings)
    {
        var apiKey = UnprotectSecret(settings.ProtectedApiKey, settings.ApiKeyProtection);
        if (!string.IsNullOrEmpty(apiKey))
        {
            settings.ApiKey = apiKey;
        }

        foreach (var provider in settings.ConfiguredProviders)
        {
            apiKey = UnprotectSecret(provider.ProtectedApiKey, provider.ApiKeyProtection);
            if (!string.IsNullOrEmpty(apiKey))
            {
                provider.ApiKey = apiKey;
            }
        }

        return settings;
    }

    private static void ProtectSecret(string secret, out string protectedValue, out string protection)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            protectedValue = "";
            protection = "";
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
            protectedValue = Convert.ToBase64String(WindowsDpapi.Protect(bytes));
            protection = DpapiCurrentUser;
            return;
        }

        // macOS / Linux fallback: persist the secret in cleartext under the
        // "plain" marker. The settings file is owned by the user account, so
        // this matches the behaviour of common CLI config tools, but it is
        // not a real protection-at-rest boundary. Encrypted protection for
        // non-Windows platforms is tracked as a post-1.0 follow-up.
        protectedValue = secret;
        protection = "plain";
    }

    private static string UnprotectSecret(string protectedValue, string protection)
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

            return string.Equals(protection, "plain", StringComparison.OrdinalIgnoreCase)
                ? protectedValue
                : "";
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
            ProviderId = settings.ProviderId,
            ProtocolId = settings.ProtocolId,
            ProviderName = settings.ProviderName,
            BaseUrl = settings.BaseUrl,
            ApiKey = settings.ApiKey,
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
            AuditLogRetentionDays = settings.AuditLogRetentionDays
        };
    }

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
