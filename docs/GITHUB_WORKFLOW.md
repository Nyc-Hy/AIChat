# GitHub Workflow

This project is managed through GitHub issues, pull requests, and CI.

## Repository Defaults

- Default branch: `master`
- License: Apache License 2.0
- Required local verification for code changes:
  - `dotnet build AIChat.sln --no-restore -m:1 -v:minimal`
  - `dotnet test tests\AIChat.Tests\AIChat.Tests.csproj --no-restore -m:1 -v:minimal`
- CI entry point: `.github/workflows/ci.yml`

## Issues

Use issues as the planning source of truth.

Recommended labels:

- `type:feature`
- `type:bug`
- `type:docs`
- `type:chore`
- `area:agent`
- `area:providers`
- `area:tools`
- `area:ui`
- `area:storage`
- `area:security`
- `good first issue`
- `help wanted`

An issue should describe the problem, expected behavior, implementation notes when useful, and verification expectations.

## Pull Requests

Open a PR for each focused change. The PR should:

- Link the issue with `Closes #123` or `Refs #123`
- Summarize user-facing behavior changes
- List verification commands and results
- Note any follow-up work intentionally left out

Use draft PRs for incomplete work. Ready PRs should pass CI before merge.

## CI Policy

The CI workflow runs on pull requests and pushes to `master`.

It performs:

1. .NET SDK setup from `global.json`
2. Restore
3. Build with single-node MSBuild
4. Test the unit test project with single-node MSBuild

Single-node build/test is intentional because this repository has been stabilized around deterministic local and CI execution.

## Branch Protection

The `master` branch should be protected:

- Require pull requests before merging.
- Require the CI build/test workflow before merging.
- Block force pushes.
- Keep direct pushes limited to maintainers.

## Merge Guidance

Before merging:

- CI is green.
- The PR has a clear summary and verification notes.
- Security-sensitive changes mention path, shell, provider, audit, or secret-handling impact.
- UI changes include a short note about the affected screen or interaction.

Prefer squash or regular merge consistently. If history readability matters more than preserving every local commit, use squash merge.

## Releases

For manual release builds:

```powershell
dotnet publish src\AIChat.App\AIChat.App.csproj -c Release -r win-x64 --self-contained false
```

For a self-contained Windows build:

```powershell
dotnet publish src\AIChat.App\AIChat.App.csproj -c Release -r win-x64 --self-contained true
```

Attach release notes that include user-facing changes, verification, and known issues.
