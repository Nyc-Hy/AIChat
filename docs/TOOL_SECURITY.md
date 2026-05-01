# Tool Security Model

## Permission Modes

Each tool has a permission mode that controls how it's executed:

| Mode | Behavior |
|---|---|
| `Disabled` | Tool is not exposed to the model |
| `AutoReadOnly` | Read-only tools run without confirmation; write/shell tools require approval |
| `ConfirmEachTime` | Every invocation requires user approval |
| `AllowForSession` | After first approval, auto-approves for the rest of the agent run |

### Default Permissions

The `AgentToolRegistry` assigns default permissions based on tool risk:

- **ReadOnly tools** (list_files, read_file, search_text, git_status, git_diff): `AutoReadOnly`
- **Write tools** (write_file, edit_file, apply_patch, git_restore_file, git_commit): `ConfirmEachTime`
- **Shell tools** (run_build, run_test, run_shell): `ConfirmEachTime`

### Project Overrides

Projects can override global tool permissions. Project overrides take precedence when merged.

## Path Protection

All file operations are confined to the project directory via `ProjectPathGuard`:

- `ResolveInsideProject()` resolves a relative path against the project root
- Rejects paths that escape the project directory (via `..` or absolute paths)
- Applied by: `ReadFileTool`, `WriteFileTool`, `EditFileTool`, `ApplyPatchTool`, `ShellCommandTool`

## Shell Sandbox

The `ShellCommandTool` has a multi-layer defense:

### Blocklist

Commands containing these patterns are rejected outright:

- Recursive delete: `rm -rf`, `Remove-Item -Recurse`, `rmdir /s`
- Force reset: `git reset --hard`, `git clean -fdx`, `git push --force`
- Disk operations: `dd if=`, `mkfs.`, `format `
- System commands: `shutdown`, `reboot`, `Stop-Computer`
- Permission escalation: `chmod 777`, `chown -R`, `Set-ExecutionPolicy`

### Allowlist

Commands starting with these prefixes are considered safe:

- Build/test: `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet run`
- Git read: `git status`, `git diff`, `git log`, `git branch`, `git show`
- Search: `rg`, `grep`, `find`
- File listing: `ls`, `dir`, `cat`, `head`, `tail`
- Info: `echo`, `pwd`, `which`, `file`, `stat`

### Timeout

Shell commands have a configurable timeout (default: 30s, max: 120s). Processes are killed on timeout.

## Write Tool Safety

### Snapshot + Hash

Before modifying a file, the tool records:

- `ContentSnapshot` — the file's content before the change
- `PostChangeHash` — SHA256 of the file's content after the change

### Conflict Detection

When rolling back changes, the system compares the current file's hash with `PostChangeHash`. If they differ, the file was modified externally and a conflict is reported.

## Audit Trail

Every significant action is recorded in the audit log:

- `ToolCallRequested` — model requested a tool call
- `ToolCallApproved` — user approved
- `ToolCallRejected` — user rejected
- `FileWritten` — file was modified
- `ShellExecuted` — shell command ran
- `VerificationRun` — verification command executed
- `AgentRunStarted/Completed/Failed/Cancelled` — run lifecycle

Audit events are stored as JSONL per project under `%APPDATA%\AIChat\audit\`.

## Approval UI

When a tool requires approval, the user sees:

- Tool name and risk badge
- Summary of what the tool will do
- Full arguments JSON
- Preview of changes (for file operations)
- Diff text (for edit operations)

Three actions: Allow This Time, Allow for Session, Reject.
