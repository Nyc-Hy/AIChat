# A2A Adapter Design

## Overview

AIChat is a local-first desktop code Agent. The A2A (Agent-to-Agent) adapter allows external agents to request AIChat to execute project-level tasks — file reads/writes, builds, tests, shell commands — while preserving all existing safety guarantees.

## Core Principles

1. **All external requests pass through the Harness.** No direct tool invocation bypasses the agent loop.
2. **Permissions are enforced identically.** External requests respect the same `ToolPermissionMode`, approval flow, and shell sandbox as interactive sessions.
3. **Audit trail is complete.** Every external request produces audit events tagged with the requesting agent's ID.
4. **Workspace protection is unconditional.** Path guards, conflict detection, and rollback safety apply regardless of request origin.

## Architecture

```
External Agent (MCP/A2A)
        |
        v
  A2A Endpoint (HTTP/gRPC)
        |
        v
  A2A Request Validator
        |
        v
  AgentHarness.RunAsync()  ← same entry point as interactive UI
        |
        v
  AgentRunner → ToolExecutionService → IAgentTool
        |
        v
  AuditLogRepository (events tagged with external agent ID)
```

## Request Flow

1. External agent sends a task request (goal, project path, constraints).
2. A2A endpoint validates the request, resolves the project, and creates an `AgentHarnessRunRequest`.
3. The request is passed to `AgentHarness.RunAsync()` with `AgentRunContext` configured for the external agent:
   - `ProjectPath` resolved from the request
   - `ToolPermissionModes` from project-level overrides
   - `RequestToolApprovalAsync` set to auto-reject (or configurable policy)
4. The harness runs the standard agent loop: plan → tool calls → verification.
5. Audit events are recorded with the external agent's ID in the `RunId` field.
6. The result (success/failure, file changes, verification output) is returned to the external agent.

## Security Model

### Tool Permission Policy

External requests use a configurable permission policy:

- **Auto-reject mode (default):** All write/shell tools are rejected. External agents can only read files and inspect the project.
- **Auto-approve with audit:** Write/shell tools are auto-approved but every invocation is logged. Suitable for trusted internal agents.
- **Interactive approval:** External requests pause and wait for the human user to approve each tool call, identical to interactive sessions.

### Path Guards

The existing `ProjectPathGuard` ensures all file operations stay within the project directory. External agents cannot escape the sandbox.

### Shell Sandbox

The existing `ShellCommandTool` blocklist and allowlist apply. External agents cannot execute destructive commands even if the permission policy allows shell access.

### Rate Limiting

External requests should be rate-limited to prevent abuse:
- Max concurrent runs per external agent
- Max tool calls per run (existing `MaxToolRounds`)
- Max total file changes per run

## Data Model Extensions

```csharp
// New field on AgentHarnessRunRequest
public string ExternalAgentId { get; init; } = "";

// New field on AgentRun
public string ExternalAgentId { get; set; } = "";

// New audit event type
public enum AuditEventType
{
    // ... existing types ...
    ExternalAgentRequest,
    ExternalAgentResponse
}
```

## MCP Integration

The A2A adapter can expose an MCP-compatible endpoint:

```csharp
public class McpToolProvider : IExternalToolProvider
{
    public string Id => "mcp-server";
    public string Name => "MCP Server";

    public async Task<IReadOnlyList<IAgentTool>> GetToolsAsync(CancellationToken ct)
    {
        // Connect to MCP server, discover tools, wrap as IAgentTool
    }
}
```

MCP tools are registered with `AgentToolRegistry.RegisterExternalProvider()` and become available alongside built-in tools.

## A2A Protocol Mapping

| A2A Concept | AIChat Mapping |
|---|---|
| Agent Card | AIChat project + enabled tools |
| Task | `AgentHarnessRunRequest` |
| Artifact | `AgentFileChange` |
| Message | `ChatMessage` |
| Part | Tool call arguments/results |

## Implementation Phases

1. **Phase 1 (current):** Interface defined (`IExternalToolProvider`), registry supports registration. No external endpoints.
2. **Phase 2:** Add `A2AEndpoint` as an HTTP listener. Implement auto-reject permission policy.
3. **Phase 3:** Add configurable permission policies. Implement rate limiting.
4. **Phase 4:** Add MCP server integration via `McpToolProvider`.
5. **Phase 5:** Full A2A protocol support with agent discovery and task delegation.

## Constraints

- A2A does not bypass tool permissions.
- A2A does not bypass workspace protection.
- A2A does not bypass audit logging.
- A2A does not bypass the verification/auto-repair loop.
- All external requests are visible in the agent run history.
