# Security Policy

AIChat can execute local tools, shell commands, git operations, and provider requests. Please report security issues privately instead of opening a public issue.

## Supported Versions

Security fixes are handled on the `master` branch until formal releases begin.

## Reporting a Vulnerability

Please report vulnerabilities through GitHub private vulnerability reporting if it is enabled for this repository. If that is unavailable, open a minimal public issue asking for a private security contact without including exploit details.

Include:

- A short description of the vulnerability
- Affected commit or version
- Steps to reproduce
- Impact and expected severity
- Any suggested fix or mitigation

## Sensitive Areas

Please treat these areas as security-sensitive:

- API key storage and redaction
- Tool approval flow
- Shell command policy
- Project path confinement
- Git mutation tools
- Audit logs and run artifacts
- Provider error handling and request payloads

## Disclosure

Do not publicly disclose a vulnerability until a fix or mitigation is available. The maintainer will coordinate disclosure timing through the security report.
