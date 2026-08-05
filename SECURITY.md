# Security

## What this app touches

It reads credentials, so it is fair to want to know exactly what it does with them.

**Files read**

| Path | Why | Written? |
|---|---|---|
| `%USERPROFILE%\.claude\.credentials.json` | OAuth access token for the usage request | Only when `claude.autoRefreshToken` is enabled — see below |
| `%USERPROFILE%\.codex\auth.json` | `exp` claim of the access token, to report sign-in validity | Never |
| `%APPDATA%\RateTray\settings.json` | This app's own configuration | Yes |
| `%APPDATA%\RateTray\cache.json` | Last readings, so a restart shows numbers at once | Yes |

`cache.json` holds only values — limit ids, percentages, reset times, plan names. No token or
credential ever reaches it. Deleting it costs nothing but a blank tray until the next poll.

Both credential files are ones the official CLIs already create and maintain. The app does not
create, copy, cache or log them, and never writes a token to its own config, to disk elsewhere,
or to its diagnostic output.

**Network**

One destination as shipped: `https://api.anthropic.com/api/oauth/usage`, and only when Claude is
enabled. That is where the Claude token goes, and nowhere else.

It is a setting rather than a constant, though, as is `claude.tokenUrl` for the refresh described
below — both so a changed endpoint can be corrected without a rebuild. So the honest version of
the sentence above is: whoever can write `settings.json` can point the token somewhere else.
Locally that grants nothing new, since the same access reads `.credentials.json` directly. It does
mean a settings file taken from someone else deserves a read before it is used, like any config
carrying a URL.

Two rules keep such a change from being a quiet one. A credential is only ever sent over https —
anything else fails the poll with *"Endpoint is not https, no token sent"* before a connection is
opened, with loopback excepted so a local mock still works. And an endpoint whose host is not the
one shipped is named in the details window and in `--once` output. Neither prevents a deliberate
change; both stop it from being one nobody notices.

Codex data does not involve the network at all from this app's side — it starts a local
`codex app-server` process and speaks JSON-RPC to it over stdio. Whatever that process does
upstream is the Codex CLI's own behaviour.

There is no telemetry, no crash reporting and no update check.

**Processes started**

`codex.exe app-server`, once per poll, killed afterwards. The path is resolved from
`codex.executablePath` if set, otherwise from the default install location and `PATH`.

## `autoRefreshToken`

Off by default. When enabled, the app refreshes the Claude OAuth token itself and writes the
result back into `.credentials.json`.

Two things to know before turning it on:

1. **The refresh path has not been exercised against the live endpoint.** The token URL and
   client id are configurable precisely so a wrong value can be corrected without a rebuild.
2. Refresh tokens usually rotate. A failed refresh could in principle leave you needing to sign
   in again. The write is atomic and preserves every other field in the file, and any failure
   falls back to "start Claude Code" without modifying anything — but off is still the safer
   default, and Claude Code keeps the token fresh on its own while it runs.

## Reporting a vulnerability

Please open a [private security advisory](../../security/advisories/new) rather than a public
issue. If you cannot, open a normal issue with only enough detail to make contact, and we will
move it somewhere private.

Expect a first response within a week. This is a spare-time project — there is no bounty and no
guaranteed timeline, but credible reports will be taken seriously and credited unless you would
rather not be.

## Scope

In scope: credential handling, the settings file, anything the app writes or transmits, and
process launching.

Out of scope: vulnerabilities in Claude Code, the Codex CLI, or the upstream APIs themselves —
report those to Anthropic and OpenAI respectively.
