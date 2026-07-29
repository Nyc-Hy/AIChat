# AIChat Product Baseline

AIChat is a Vibe Coding tool. The product baseline is a small, clear, fast coding assistant that helps users complete repository tasks with high execution accuracy, low token cost, and visible context economics.

## Product Standard

AIChat should optimize for:

- Clear and simple UI, with the Avalonia desktop app as the primary interface.
- Explicit project and conversation boundaries.
- Accurate task execution and conservative file mutation.
- Low token usage by default.
- Fast feedback and bounded tool loops.
- Accurate context-size visibility before and during each run.
- Transparent per-session usage: input tokens, output tokens, cache hits, and estimated cost signals where provider data allows.

The product should not feel like a generic agent framework dashboard. It should feel like a focused coding workbench.

## UI Baseline

The Avalonia UI should stay quiet, dense, and work-focused:

- The first screen must be the usable coding workspace, not a landing page.
- Project, active provider, active model, selected conversation, and run status must always be easy to identify.
- Each conversation should have a clear title, project association, timestamp, and run outcome.
- Risky actions should be explicit: file writes, shell commands, tests, builds, and Git mutations require visible approval.
- Status and metrics should be glanceable first, then explainable on hover.

Use information tooltips for details that would clutter the main surface:

- Context budget details.
- Included and omitted context files.
- Input token estimate.
- Output token count.
- Cache-hit estimate or provider-reported cache hit.
- Tool-round count.
- Runtime duration.
- Verification result details.

## Session Metrics

Every completed session should expose a compact usage summary:

| Metric | Meaning | UI Requirement |
|---|---|---|
| Context size | Estimated prompt context sent to the model | Always visible before send and after run |
| Input tokens | Tokens sent to the provider, including system, conversation, context, and tool results | Show as a compact number; tooltip explains composition |
| Output tokens | Tokens produced by the assistant/model | Show in session summary |
| Cache hit | Provider-reported cache hit when available; otherwise cache-friendly estimate | Show as percent or "unknown"; tooltip explains source |
| Tool rounds | Number of model/tool loop rounds | Show in run summary |
| Runtime | Wall-clock time for the run | Show in run summary |
| Verification | Build/test result if run | Show pass/fail and command count |

If a provider does not return exact token or cache data, AIChat should label estimates clearly. Never present estimated usage as exact provider billing data.

## Execution Baseline

Default behavior should favor predictable completion:

- Standard mode uses a single-agent loop.
- Planner and sub-agents are off by default.
- Auto-fix and memory writes are off by default.
- Read-only discovery should happen before mutation.
- Mutation tools should remain approval-gated.
- Verification should be visible and easy to trigger, but not forced for every small task.

Execution quality is judged by:

- Whether the task outcome matches the user request.
- Whether unnecessary files and commands were avoided.
- Whether the run stayed within a reasonable context and token budget.
- Whether the user can inspect what changed and why.

## Context Baseline

Context is a product feature, not an implementation detail.

AIChat should show:

- Current context budget.
- Estimated tokens selected for this task.
- Files included in context.
- Relevant files omitted due to budget or low confidence.
- Pinned context, memories, and input artifacts included.
- Cache-friendly prompt hints where applicable.

The user should be able to understand why a file was included or omitted without reading logs.

## Conversation Baseline

Each conversation should be explicit and reviewable:

- One conversation belongs to one project.
- A conversation should preserve user prompts, assistant replies, tool calls, approvals, verification, and final outcome.
- Follow-up messages should reuse relevant conversation context without silently expanding token usage.
- Session summaries should make input, output, cache, context, and verification visible.

## Non-Goals

The baseline excludes:

- Large multi-agent dashboards as the default UI.
- Always-on background autonomy.
- Hidden memory writes.
- Unbounded context accumulation.
- UI surfaces dominated by benchmark, plugin, or framework internals.
