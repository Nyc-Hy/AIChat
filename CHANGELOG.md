# Changelog

All notable changes to AIChat will be documented in this file.

This project follows a simple date-based changelog until formal versioned releases begin.

## Unreleased

### Added

- GitHub CI, issue templates, pull request template, and contribution workflow.
- Provider configuration validation, connection testing, and standardized provider error classification.
- Agent run reliability diagnostics for cancellation, tool failures, and recovery guidance.
- Secret redaction and safer local settings serialization.

### Changed

- Build and test guidance now uses single-node .NET commands for deterministic local and CI execution.
- GitHub repository management is now documented in `docs/GITHUB_WORKFLOW.md`.

### Security

- API keys are protected at rest and redacted from diagnostics.
- Tool traces, audit records, provider events, and agent artifacts redact sensitive values.
- Shell and path handling are constrained by allowlists, blocklists, and project path guards.
