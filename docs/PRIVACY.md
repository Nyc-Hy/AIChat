# Privacy Policy

> **Last updated**: 2026-08-03 · **Applies to**: AIChat 1.0.0 Beta and later
>
> AIChat is a local coding assistant. The desktop app runs entirely on your
> machine and is the only product surface. There is no AIChat cloud account,
> no AIChat-hosted telemetry, and no AIChat-managed conversation storage.

## TL;DR

- **Your code, your prompts, your conversations never leave your machine.**
  The only network traffic is the LLM API call (you choose the provider and
  the API key; AIChat does not relay through any AIChat server).
- **Your API key is stored locally** in the platform credential vault
  (macOS Keychain, Linux Secret Service, Windows DPAPI). The 0.5 plaintext
  fallback has been retired; if the platform vault is unavailable, the key is
  session-only and the UI shows a warning.
- **No telemetry, no analytics, no crash reporting to a remote server.**
  Crash logs are written to `<dataDir>/crash.log` and never uploaded.
  See [`TELEMETRY.md`](TELEMETRY.md) for the explicit "no" list.
- **No account, no login, no license key, no online activation.** The app
  checks the platform package signature locally; it does not phone home.

## What data is stored locally

Everything AIChat stores lives under one directory, which the OS picks by
convention:

| OS      | Path                                                   |
|---------|--------------------------------------------------------|
| macOS   | `~/Library/Application Support/AIChat/`                |
| Linux   | `~/.config/AIChat/`                                    |
| Windows | `%LOCALAPPDATA%\AIChat\` (resolved via `Environment.SpecialFolder.ApplicationData`) |

Override the path for demos / UI tests / support sessions with
`AICHAT_ISOLATED_DATA_ROOT=<absolute path>`. The isolated profile never opens
the production settings or the platform credential vault.

Files in the data directory:

| File | What it contains | Notes |
|---|---|---|
| `settings.json` | Provider config, model id, base URL, tool permission modes, last active project / conversation, theme, etc. | The `protectedApiKey` and `apiKeyProtection` fields point into the platform vault; the plaintext is **not** on disk. |
| `projects.json` | Workspace project list (id, name, folders, primary folder, AGENTS.md path) | No file contents are stored; only paths and metadata. |
| `sessions.json` | Conversation history (Standalone + Project-scoped sessions, message stream, tool calls, verification results) | Stays on your machine. No automatic upload. |
| `scheduled-tasks.json` / `scheduled-task-runs.json` | Scheduled task definitions + run history | Local-only. |
| `sites.json` / `site-deployments.json` | Sites definitions + per-site deployment history | Local-only. |
| `plugins/<plugin-id>/plugin.json` | Per-plugin manifest read by the registry | Bundled plugin content lives in the install package, not the data dir. |
| `pending-attachments/` | Image staging area for `⌘V` paste-and-attach in the composer | Auto-cleaned on next launch. |
| `background-processes.json` | Sites preview / Scheduled run process registry | Restart-recovery walks this file to mark Crashed rows. |
| `crash.log` | Append-only log of unhandled exceptions with timestamp + stack + OS / runtime version | See `TELEMETRY.md`. |
| `audit.log` (rotated) | Tool trace + provider events + agent artefacts, with sensitive values redacted via `SensitiveDataRedactor` | Default 5 MB per file, 30 days retention. Configurable in Settings. |

The data directory is **not** uploaded anywhere. The desktop app does not
expose a "sync" / "share" / "publish" action that touches the data directory.

## What data leaves your machine

The only network traffic AIChat generates is:

1. **LLM API calls** — to the `BaseUrl` you configured in Settings. By
   default that is the MiniMax endpoint at `https://api.minimax.io/v1`.
   You can change it to any other OpenAI-compatible endpoint (self-hosted
   proxies, internal mirrors). The request includes:
   - the prompt / conversation history
   - tool definitions
   - your API key (in the `Authorization: Bearer` header)
   - the model id you selected

   What you send to the model is governed by the model provider's privacy
   policy, not by AIChat. AIChat does not see or store the response other
   than to display it in the conversation activity stream and (if you have
   audit logging on) to write a redacted copy to `audit.log`.

2. **OS-level updates** (when present) — if you opt into a future in-app
   auto-update flow, the app will fetch the new release from a configurable
   update URL. 1.0 Beta does not include an in-app updater; the user
   downloads new releases from GitHub manually.

That is the complete list. There is no other outbound traffic.

## Credential storage

API keys are stored in the platform credential vault, not in `settings.json`:

- **macOS**: Keychain, service name `AIChat`, account name `settings-api-key`
  and `provider-<id>-api-key` per configured provider. The first time the app
  reads a key, macOS shows a "Keychain Access" dialog asking the user to
  allow or deny. The user's "always allow" choice is per-keychain-entry and
  persists across app launches.

- **Linux**: Secret Service (via `secret-tool`), service name `AIChat`. The
  desktop's Secret Service agent (gnome-keyring, KWallet, KeePassXC) shows
  a similar first-time prompt.

- **Windows**: DPAPI current-user scope. The key is encrypted with a key
  derived from the current Windows user account; only the same user on the
  same machine can decrypt it.

If the platform vault is unavailable, AIChat does **not** fall back to
writing the plaintext key to `settings.json`. Instead the key is held in
memory for the lifetime of the process and the UI shows a banner:
"当前 keychain 不可用 — API key 仅本次会话有效，重启后需重新输入".

A `2026-08-03` addition lets daily-driver users skip the platform vault
entirely by exporting `AICHAT_API_KEY` (or `AICHAT_PROVIDER_<NAME>_API_KEY`)
in the environment. The env var is the source of truth for that process;
`settings.json`'s keychain reference is left untouched so `unset` falls
back to the keychain without losing the stored secret. See
[AGENTS.md §"API key 访问"](../../AGENTS.md) for the design.

## What we do **not** collect

To be explicit (so a privacy / compliance reviewer does not have to guess):

- ❌ No usage analytics, no "feature X is used by N% of users" telemetry.
- ❌ No crash reports uploaded to a remote server. The crash log is local;
  if you want to share it, attach `crash.log` to a GitHub issue manually.
- ❌ No prompt or response content uploaded to a remote server. LLM
  traffic goes only to the `BaseUrl` you configured.
- ❌ No API keys in crash logs, audit logs, or error messages. The
  `SensitiveDataRedactor` strips keys before any diagnostic write.
- ❌ No fingerprinting, no device id, no install-id sent over the wire.
- ❌ No third-party SDKs that phone home. The Avalonia 12 / .NET 10 stack
  does not include any analytics by default.
- ❌ No advertising, no upsell, no "upgrade to Pro" prompts.

## GDPR / CCPA position

- **Data controller**: you are. AIChat does not store your data on any
  server; there is no "AIChat" entity that processes your data on its own
  behalf.
- **Right to access / portability / erasure**: the entire data set lives in
  one directory. Copy / back up / delete it as you would any other user
  data folder. There is no server-side copy.
- **Right to rectification**: edit `settings.json` directly with a text
  editor; it is JSON, no proprietary format.
- **Children's data**: AIChat is a developer tool. It is not directed at
  children. We do not knowingly collect data from anyone; that said, the
  app is not gated behind an age check.
- **International transfers**: the only outbound traffic is the LLM API
  call. Where that data is processed depends on the LLM provider you
  configured, not on AIChat.

## Contact

- **Issues / questions / privacy requests**: open a GitHub issue on the
  AIChat repository. There is no support email; the project is
  community-maintained.
- **Security disclosures**: see [`SECURITY.md`](SECURITY.md) for the
  responsible-disclosure policy.
- **Source code**: the entire app is open source under
  [Apache License 2.0](../LICENSE). You can audit every line that touches
  your data.

## Changes to this policy

- 2026-08-03: 1.0 Beta initial release. Added the env var override
  section, the explicit "no telemetry" list, the platform vault
  unavailability fallback, and the data directory table.
- Future changes are tracked in [`CHANGELOG.md`](../CHANGELOG.md) under
  the "Security" subsection of each release.
