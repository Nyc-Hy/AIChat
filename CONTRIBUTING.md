# Contributing to AIChat

AIChat is managed through GitHub issues, pull requests, and CI checks.

By contributing, you agree that your contributions are licensed under the Apache License 2.0.

## Workflow

1. Open or pick a GitHub issue for the work.
2. Create a short branch from `master`.
3. Keep the change focused on one feature, fix, or documentation update.
4. Run local verification before opening a pull request.
5. Open a pull request that links the issue and explains the verification.

## Branch Names

Use short, descriptive names:

```text
feature/provider-health-check
fix/agent-run-cancellation
docs/github-workflow
chore/update-ci
```

Automation branches may use the `codex/` prefix.

## Commits

Use imperative commit messages:

```text
Harden provider configuration and errors
Improve agent run reliability diagnostics
Document GitHub workflow
```

Do not include local secrets, logs, installers, `bin/`, `obj/`, `.vs/`, `.tools/`, or generated artifacts.

## Pull Requests

Each PR should include:

- What changed
- Why it changed
- Tests or verification run
- Screenshots only when UI behavior changed
- Linked issue, if one exists

Prefer draft PRs for work in progress. Mark a PR ready only after the local build and tests pass.

## Verification

For code changes:

```powershell
dotnet build AIChat.sln --no-restore -m:1 -v:minimal
dotnet test tests\AIChat.Tests\AIChat.Tests.csproj --no-restore -m:1 -v:minimal
```

For documentation-only changes:

```powershell
git diff --check
```

## Review Standard

Reviews should focus on:

- Correctness and regressions
- Safety around tools, paths, shell execution, git operations, and secrets
- Test coverage for changed behavior
- Consistency with existing architecture
- Clear user-facing error messages

Avoid mixing large refactors with feature work unless the refactor is necessary for the change.

## Security

Do not report vulnerabilities with exploit details in public issues. Follow [SECURITY.md](SECURITY.md).
