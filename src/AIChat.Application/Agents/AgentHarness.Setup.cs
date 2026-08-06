using AIChat.Domain.Chat;

namespace AIChat.Application.Agents;

// Run-record + execution-policy setup — split out from the
// main AgentHarness partial so the orchestration file stays
// focused on the run loop. CreateRunRecord hydrates a new
// AgentRun from the request and appends it to the
// conversation's AgentRuns list (called from RunAsync as
// stage 0), and ResolveExecutionPolicy classifies the task,
// builds the policy, folds in historical adjustments, and
// projects the resolved values back onto the run record
// (called from RunAsync as stage 0.5). Both are instance
// methods because they need access to _taskClassifier /
// _executionPolicyBuilder.
public sealed partial class AgentHarness
{
    // Stage 0 of RunAsync: hydrate the AgentRun record from the
    // request and register it on the conversation. Everything in
    // the record is request-derived (Goal, ProjectPath, Model,
    // tools, permissions, workspace snapshot, prep status,
    // context-pack stats) plus the always-default flags
    // (RequiresProjectMutation starts false and flips on the
    // first mutation tool) and timestamps. The one side-effect
    // — appending to AgentRuns — happens here so downstream
    // helpers that scan history (ApplyHistoricalAdjustments,
    // UI consumers) already see this run by the time RunStarted
    // is yielded.
    private AgentRun CreateRunRecord(AgentHarnessRunRequest request)
    {
        var run = new AgentRun
        {
            ConversationId = request.Conversation.Id,
            UserMessageId = request.UserMessageId,
            AssistantMessageId = request.AssistantMessageId,
            Goal = request.Goal,
            ProjectPath = request.Context.ProjectPath,
            Model = request.Settings.Model,
            EnabledTools = request.Context.EnabledToolIds.ToList(),
            ToolPermissionModes = request.Context.ToolPermissionModes.ToDictionary(
                entry => entry.Key, entry => entry.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            WorkspaceBranch = request.WorkspaceBranch,
            WorkspaceChangeCountAtStart = request.WorkspaceChangeCountAtStart,
            WorkspaceChangesWereTruncated = request.WorkspaceChangesWereTruncated,
            ProjectPreparationSucceeded = request.Context.ProjectPreparationSucceeded,
            ProjectPreparationSummary = request.Context.ProjectPreparationSummary,
            ProjectAgentsAvailableAtStart = request.Context.ProjectAgentsAvailable,
            ProjectVerificationCommandCountAtStart = request.Context.ProjectVerificationCommandCount,
            MaxToolRounds = request.Context.MaxToolRounds,
            ContextEstimatedTokens = request.ContextPack?.EstimatedTokens ?? 0,
            ContextRefCount = request.ContextPack?.ToPromptRefs().Count ?? 0,
            RequiresProjectMutation = false,
            ContinuedFromRunId = request.ContinuedFromRunId,
            RetriedFromRunId = request.RetriedFromRunId,
            StartedAt = DateTimeOffset.Now
        };
        request.Conversation.AgentRuns.Add(run);
        return run;
    }

    // Stage 0.5 of RunAsync: classify the task, build the
    // execution policy, fold in historical adjustments, and
    // project the resolved values back onto the run record so
    // RunStarted already carries the right MaxToolRounds /
    // TaskComplexity / ExecutionPolicySummary. The history
    // filter (`item.Id != run.Id`) is needed because
    // CreateRunRecord already appended the new run to
    // AgentRuns — without the filter, ApplyHistoricalAdjustments
    // would see the just-created run in its own history and
    // double-count budget pressure on the very first run of a
    // conversation.
    private AgentTaskExecutionPolicy ResolveExecutionPolicy(AgentHarnessRunRequest request, AgentRun run)
    {
        var taskComplexity = _taskClassifier.Classify(request.Goal, request.Context);
        var policy = _executionPolicyBuilder.Build(
            taskComplexity, request.Context, request.ContextPack,
            !string.IsNullOrWhiteSpace(request.ContinuedFromRunId));
        policy = ApplyHistoricalAdjustments(
            policy, request.Context,
            request.Conversation.AgentRuns.Where(item => item.Id != run.Id).ToList());
        run.MaxToolRounds = policy.MaxToolRounds;
        run.TaskComplexity = policy.Complexity.ToString();
        run.ExecutionPolicySummary = AgentExecutionPolicySummaryBuilder.Build(policy);
        return policy;
    }
}
