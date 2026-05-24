using AIChat.Abstractions.Configuration;
using AIChat.Application.Configuration;

namespace AIChat.Tests.Configuration;

public sealed class AdvancedSettingsServiceTests
{
    [Fact]
    public void Normalize_ClampsAdvancedSettingsToSupportedRanges()
    {
        var settings = new AppSettings
        {
            AgentMaxToolRounds = 0,
            MaxAutoFixRounds = 99,
            RetryMaxAttempts = 99,
            MaxOutputTokens = 12,
            ConversationContextRatio = 2.0,
            AuditLogRetentionDays = 0,
            AuditLogMaxFileSizeBytes = 128
        };

        AdvancedSettingsService.Normalize(settings);

        Assert.Equal(1, settings.AgentMaxToolRounds);
        Assert.Equal(10, settings.MaxAutoFixRounds);
        Assert.Equal(10, settings.RetryMaxAttempts);
        Assert.Equal(256, settings.MaxOutputTokens);
        Assert.Equal(1.0, settings.ConversationContextRatio);
        Assert.Equal(1, settings.AuditLogRetentionDays);
        Assert.Equal(5 * 1024 * 1024, settings.AuditLogMaxFileSizeBytes);
    }

    [Fact]
    public void Normalize_PreservesValuesInsideSupportedRanges()
    {
        var settings = new AppSettings
        {
            AgentMaxToolRounds = 25,
            MaxAutoFixRounds = 0,
            RetryMaxAttempts = 4,
            MaxOutputTokens = 8192,
            ConversationContextRatio = 0.6,
            AuditLogRetentionDays = 30,
            AuditLogMaxFileSizeBytes = 8 * 1024 * 1024
        };

        AdvancedSettingsService.Normalize(settings);

        Assert.Equal(25, settings.AgentMaxToolRounds);
        Assert.Equal(0, settings.MaxAutoFixRounds);
        Assert.Equal(4, settings.RetryMaxAttempts);
        Assert.Equal(8192, settings.MaxOutputTokens);
        Assert.Equal(0.6, settings.ConversationContextRatio);
        Assert.Equal(30, settings.AuditLogRetentionDays);
        Assert.Equal(8 * 1024 * 1024, settings.AuditLogMaxFileSizeBytes);
    }

    [Fact]
    public void NormalizeAuditLogMaxFileSizeMegabytes_NeverReturnsLessThanOne()
    {
        Assert.Equal(1, AdvancedSettingsService.NormalizeAuditLogMaxFileSizeMegabytes(0));
        Assert.Equal(3, AdvancedSettingsService.NormalizeAuditLogMaxFileSizeMegabytes(3));
    }
}
