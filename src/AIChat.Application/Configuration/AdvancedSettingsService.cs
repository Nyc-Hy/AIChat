using AIChat.Abstractions.Configuration;

namespace AIChat.Application.Configuration;

public static class AdvancedSettingsService
{
    public const int MinAgentMaxToolRounds = 1;
    public const int MaxAgentMaxToolRounds = 100;
    public const int MinAutoFixRounds = 1;
    public const int MaxAutoFixRounds = 10;
    public const int MinRetryMaxAttempts = 0;
    public const int MaxRetryMaxAttempts = 10;
    public const int MinOutputTokens = 256;
    public const int MaxOutputTokens = 32768;
    public const double MinConversationContextRatio = 0.3;
    public const double MaxConversationContextRatio = 1.0;
    public const int MinAuditLogRetentionDays = 1;
    public const int MaxAuditLogRetentionDays = 365;
    public const long MinAuditLogMaxFileSizeBytes = 1024 * 1024;
    public const long DefaultAuditLogMaxFileSizeBytes = 5 * 1024 * 1024;

    public static void Normalize(AppSettings settings)
    {
        settings.AgentMaxToolRounds = NormalizeAgentMaxToolRounds(settings.AgentMaxToolRounds);
        settings.MaxAutoFixRounds = NormalizeMaxAutoFixRounds(settings.MaxAutoFixRounds);
        settings.RetryMaxAttempts = NormalizeRetryMaxAttempts(settings.RetryMaxAttempts);
        settings.MaxOutputTokens = NormalizeMaxOutputTokens(settings.MaxOutputTokens);
        settings.ConversationContextRatio = NormalizeConversationContextRatio(settings.ConversationContextRatio);
        settings.AuditLogRetentionDays = NormalizeAuditLogRetentionDays(settings.AuditLogRetentionDays);
        settings.AuditLogMaxFileSizeBytes = NormalizeAuditLogMaxFileSizeBytes(settings.AuditLogMaxFileSizeBytes);
    }

    public static int NormalizeAgentMaxToolRounds(int value)
    {
        return Math.Clamp(value, MinAgentMaxToolRounds, MaxAgentMaxToolRounds);
    }

    public static int NormalizeMaxAutoFixRounds(int value)
    {
        return Math.Clamp(value, MinAutoFixRounds, MaxAutoFixRounds);
    }

    public static int NormalizeRetryMaxAttempts(int value)
    {
        return Math.Clamp(value, MinRetryMaxAttempts, MaxRetryMaxAttempts);
    }

    public static int NormalizeMaxOutputTokens(int value)
    {
        return Math.Clamp(value, MinOutputTokens, MaxOutputTokens);
    }

    public static double NormalizeConversationContextRatio(double value)
    {
        return Math.Clamp(value, MinConversationContextRatio, MaxConversationContextRatio);
    }

    public static int NormalizeAuditLogRetentionDays(int value)
    {
        return Math.Clamp(value, MinAuditLogRetentionDays, MaxAuditLogRetentionDays);
    }

    public static long NormalizeAuditLogMaxFileSizeBytes(long value)
    {
        return value < MinAuditLogMaxFileSizeBytes
            ? DefaultAuditLogMaxFileSizeBytes
            : value;
    }

    public static long NormalizeAuditLogMaxFileSizeMegabytes(long value)
    {
        return Math.Max(1, value);
    }
}
