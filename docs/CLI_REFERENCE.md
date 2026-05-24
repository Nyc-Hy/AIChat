# AIChat CLI Reference

AIChat 1.0 is a cross-platform CLI/TUI coding assistant for local repository work.

## Global Options

```bash
aichat <command> [options]
```

- `--data-dir <path>`: use a custom settings/projects directory.
- `-h`, `--help`: show help.

Running `aichat` with no command starts the TUI in the current directory. It is equivalent to:

```bash
aichat tui --project .
```

## Version

```bash
aichat --version
```

Prints the CLI version.

## Doctor

```bash
aichat doctor
```

Prints local readiness information: version, .NET runtime, OS, configured provider, tool count, project count, and status.

## Models

```bash
aichat models
aichat models --provider deepseek
```

Lists supported providers and models, including model profile defaults.

## Provider Configuration

```bash
aichat config show
aichat config list
aichat config set-provider --provider deepseek --api-key <key> [--model deepseek-chat] [--base-url <url>]
aichat config use --provider deepseek [--model deepseek-chat]
aichat config test [--provider deepseek]
```

`config test` performs a small non-mutating provider readiness check before you spend tokens on a coding task.

## Projects

```bash
aichat init [--project <path>] [--name <name>]
aichat projects list
```

`init` registers a repository and detects verification commands when possible.

## Context Diagnostics

```bash
aichat context [goal] [--project <path>] [--tokens 1200] [--max-files 500]
```

Prints model-free context diagnostics:

- project health and profile
- file index distribution
- estimated context token budget
- included files
- omitted relevant files
- verification commands
- cache hints

Use this before `ask` or `tui` when you want to understand what context the assistant is likely to use.

## One-Shot Coding Task

```bash
aichat ask "fix the failing test" [--project <path>] [--mode fast|standard|deep] [--plain] [--yes] [--no-write] [--verify]
```

Modes:

- `fast`: small questions and low-cost tasks.
- `standard`: default coding loop.
- `deep`: harder tasks with a larger tool budget and verification.

Safety flags:

- `--yes`: approve tool calls for the session.
- `--no-write`: disable mutation-oriented tools.
- `--verify`: enable verification after a task.
- `--plain`: use plain chat even if the model supports tools.

## Interactive TUI

```bash
aichat tui [--project <path>] [--mode fast|standard|deep] [--plain] [--yes] [--no-write] [--verify]
```

TUI commands:

```text
/help
/mode fast|standard|deep
/context [goal]
/yes
/plain
/no-write
/verify
/status
/exit
```

By default, write, shell, build/test, and git mutation tools require explicit approval.
