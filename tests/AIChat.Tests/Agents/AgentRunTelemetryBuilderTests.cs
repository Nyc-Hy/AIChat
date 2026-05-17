using AIChat.Application.Agents;
using AIChat.Domain.Chat;

namespace AIChat.Tests.Agents;

public sealed class AgentRunTelemetryBuilderTests
{
    [Fact]
    public void Build_ComputesEfficiencyAndSavedChars()
    {
        var run = new AgentRun
        {
            Status = AgentRunStatus.Completed,
            QualityScore = 90,
            ContextEstimatedTokens = 1200,
            ContextRefCount = 4,
            ModelCallCount = 2,
            ToolCallCount = 5,
            ToolApprovalRequiredCount = 1,
            ToolApprovalRejectedCount = 0,
            CompletionEvidenceStatus = "satisfied",
            FileChanges =
            {
                new AgentFileChange { ToolCallId = "tool-1", Path = "src/App.cs" }
            },
            Verifications =
            {
                new AgentVerification { IsSuccess = true },
                new AgentVerification { IsSuccess = false }
            },
            Artifacts =
            {
                new AgentArtifact
                {
                    Content = new string('a', 1000),
                    Summary = new string('b', 100),
                    Metadata = { ["wasSummarized"] = "true" }
                }
            }
        };

        var telemetry = AgentRunTelemetryBuilder.Build(run);

        Assert.Equal(1200, telemetry.EstimatedPromptTokens);
        Assert.Equal(2.5, telemetry.ToolCallsPerModelCall);
        Assert.Equal(0.5, telemetry.VerificationPassRate);
        Assert.Equal(900, telemetry.EstimatedSavedChars);
        Assert.Equal(1, telemetry.SummarizedArtifactCount);
        Assert.Equal(AgentRunOutcomeKind.VerificationFailed, AgentRunTelemetryBuilder.ClassifyOutcome(run));
    }

    [Fact]
    public void ClassifyOutcome_ReportsEvidenceRiskBeforeSuccess()
    {
        var run = new AgentRun
        {
            Status = AgentRunStatus.Completed,
            QualityScore = 90,
            CompletionEvidenceStatus = "risk"
        };

        var outcome = AgentRunTelemetryBuilder.ClassifyOutcome(run);

        Assert.Equal(AgentRunOutcomeKind.EvidenceRisk, outcome);
    }
}
