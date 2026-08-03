# Telemetry

> **TL;DR**: AIChat does not collect telemetry. There is no opt-in / opt-out
> because there is no telemetry to opt into. This page exists to make that
> commitment explicit and to be the canonical reference when a privacy or
> compliance reviewer asks "what does the app send back to you?".

## What "no telemetry" means here

A "telemetry" call, for the purposes of this document, is **any outbound
network request the AIChat application code initiates on its own** —
i.e. not a request the user explicitly issued (such as clicking a button
that opens a webpage) and not a request the user explicitly configured
(such as changing the LLM `BaseUrl` in Settings).

Under that definition, AIChat 1.0 Beta makes **zero** such calls. The
application's outbound traffic is exclusively the LLM API call to the
`BaseUrl` you configured; the application does not maintain a separate
side-channel to any AIChat-controlled endpoint.

## Specific things the app does not do

To make the commitment greppable for a reviewer with a packet sniffer,
here is the explicit list of behaviours that **are not present** in
the 1.0 Beta code base (each is also pinned by an automated test or
by a code-comment contract in the file that would have to add it):

- ❌ **No first-launch ping.** The app does not call home to register an
  install, validate a license, or report a session.
- ❌ **No usage metrics.** No "the user invoked `run_shell` N times in this
  session" counters, no "the user picked Settings 3 times" analytics.
- ❌ **No crash reports to a remote server.** Unhandled exceptions are
  appended to `<dataDir>/crash.log`; nothing is uploaded. If you want
  the maintainer to see a crash, attach the file to a GitHub issue manually.
- ❌ **No prompt or response content upload.** LLM traffic goes only to the
  `BaseUrl` you configured. AIChat does not maintain a relay or proxy.
- ❌ **No API keys in crash logs / audit logs / error messages.** The
  `SensitiveDataRedactor` strips them before any diagnostic write.
- ❌ **No fingerprinting or device id.** No machine-derived id, no
  install-id, no random-and-stored UUID.
- ❌ **No third-party SDKs that phone home.** The Avalonia 12 / .NET 10
  stack does not include any analytics by default; the project does not
  add any.
- ❌ **No advertising or upsell.** No "AIChat Pro" prompts, no upgrade
  nudges, no cross-promotion of other products.
- ❌ **No auto-update channel.** 1.0 Beta does not have an in-app updater.
  When one ships (post-1.0), it will be a configurable GitHub Releases
  fetch and the source will be auditable.

## What the app does send

The complete list of outbound network traffic the application code
initiates on its own is:

1. **LLM API call** to the `BaseUrl` in your Settings. That request
   includes your prompt, the conversation history the model needs to
   see, the tool definitions, your API key (in the
   `Authorization: Bearer` header), and the model id you selected.
   What the provider does with that data is governed by the
   provider's own privacy policy, not by AIChat.
2. **OS-level DNS / TLS handshakes** for the above. Standard network
   stack, not an application-level call.
3. **Optional: a `git fetch` / `git push` if you ask the agent to
   perform a git operation against a remote.** The git transport is
   driven by the `git` binary on your `PATH`; AIChat does not wrap or
   proxy it.

That is the complete list.

## Crash log

The `crash.log` file is local-only. It contains:

- UTC timestamp with millisecond precision
- The hook that caught the exception (e.g. `AppDomain.UnhandledException`)
- The exception's full type name, message, and stack trace
- The OS version and .NET runtime version
- **Nothing else.** No machine name, no user name, no install id, no
  prompt content, no API key, no IP address.

The log is at `<dataDir>/crash.log` (see [`PRIVACY.md`](PRIVACY.md) for the
data directory by OS). After a crash, the next launch shows a one-time
toast pointing at the file. Nothing is uploaded automatically.

If you want a maintainer to look at a crash, the recommended workflow is:

1. Open the GitHub issue tracker.
2. Attach `crash.log` to the issue (or paste the relevant `==== ... ====`
   block if the file is large).
3. Maintainers will not have any other data; the report starts and ends
   with the file you attached.

## How to verify

If you want to confirm the no-telemetry commitment yourself, the
following are sufficient checks for a 1.0 Beta build:

1. **Network monitor** (Little Snitch / Wireshark / `nettop` /
   `tcpdump`): launch the app, send a task, and observe. You should see
   only HTTPS connections to the `BaseUrl` you configured, plus any
   local TCP traffic to `localhost` if you started a Sites preview.
2. **Code audit**: the project is open source under Apache 2.0. Grep the
   source tree for `HttpClient`, `WebRequest`, `Socket`, `UdpClient`,
   `TcpClient`, `Dns.GetHostEntry`. Each match should be traceable to
   the LLM call path (`AIChat.Providers.OpenAI`) or to a clearly named
   helper for git transport. There is no "telemetry client" namespace.
3. **Process monitor** (on Windows) / `lsof -i` (on macOS / Linux) while
   the app is running, to confirm there is no background process
   maintaining an open connection to anything other than the configured
   `BaseUrl`.

If you find a connection that the above checks do not explain, please
open a security disclosure per [`SECURITY.md`](SECURITY.md) — that
would be a bug, and the maintainer will treat it as one.

## Future changes

If a future version of AIChat adds any of the above behaviours (e.g. an
opt-in crash reporting service, an in-app updater, an opt-in usage
study), this document will be updated **before** the code lands and the
change will be flagged in the `CHANGELOG.md` "Security" subsection of
the relevant release. The default will remain "off" for any such
behaviour; opt-in is the only acceptable path.

## Contact

- **Security disclosures**: see [`SECURITY.md`](SECURITY.md).
- **Privacy questions**: open a GitHub issue.
- **Source code**: the entire app is open source under
  [Apache License 2.0](../LICENSE).
