# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows tray indicator showing Claude and Codex subscription usage as Core Temp-style
numbers — one `NotifyIcon` per limit, each drawing its percentage straight into the icon.
Windows-only by design (WinForms, GDI+, the HKCU Run key).

## Commands

```powershell
dotnet build RateTray.sln
dotnet test tests/RateTray.Tests     # unit tests, no desktop needed
dotnet test tests/RateTray.E2E       # drives the real .exe; skips itself when headless

# Single test
dotnet test tests/RateTray.Tests --filter "FullyQualifiedName~PaletteTests"

# Poll both providers and print every limit id the account exposes.
# WinExe detaches from the shell, so capture output instead of using `>`:
Start-Process src/RateTray/bin/Debug/net9.0-windows/RateTray.exe `
  -ArgumentList "--once" -Wait -NoNewWindow -RedirectStandardOutput out.txt

dotnet run --project src/RateTray -- --details     # just the fly-out
dotnet run --project src/RateTray -- --settings    # just the settings dialog

pwsh tools/New-AppIcon.ps1                              # regenerate app.ico
dotnet publish src/RateTray -c Release -r win-x64  # single-file release
```

State lives at `%APPDATA%\RateTray\` — `settings.json` and `cache.json`, neither in the
repo. Delete `settings.json` to re-run first-run discovery. A single-instance mutex makes a
second launch exit silently, so kill the running process before starting a rebuilt binary.

## Where the numbers come from

Neither CLI has a `usage` subcommand; both paths below were found by inspection and are the
load-bearing part of this project.

**Claude** — `GET https://api.anthropic.com/api/oauth/usage`, bearer token read fresh from
`~/.claude/.credentials.json` on every poll (Claude Code refreshes it in place while it runs).
Headers `anthropic-beta: oauth-2025-04-20` and `anthropic-version: 2023-06-01` are required.
The response's `limits[]` array is the source of truth — it already contains every window
(`session`, `weekly_all`, `weekly_scoped` per model). Do not reconstruct these from the
`five_hour`/`seven_day` top-level objects. **The endpoint rate-limits**: repeated manual polling
during development will earn a 429.

**Codex** — `codex app-server` speaks JSON-RPC over stdio; the method is
`account/rateLimits/read`. Discover the protocol with
`codex app-server generate-json-schema --experimental --out <dir>`. Two constraints:
`initialize` must be sent first, and **stdin has to stay open until the reply arrives** —
closing it makes the server exit before answering. The server is spawned per poll and killed
after; a short-lived process cannot wedge and needs no reconnect logic.

`~/.codex/sessions/**/rollout-*.jsonl` also carries a `token_count` event with `rate_limits`,
but it is only as fresh as the last Codex run — useful for inspecting the payload shape
offline, not as a data source.

## Architecture

`IUsageProvider` → `ProviderResult` → `LimitReading`. Each provider normalises its own payload
into `LimitReading`, so nothing downstream knows which service a value came from. `TrayApp`
polls providers in parallel and drives icons, hover cards, toasts and the details window off
one dictionary keyed by `LimitReading.Id`.

**Reading ids are discovered, never hardcoded.** Which windows exist depends on the plan —
per-model limits like `claude.weekly_scoped.fable` only appear on some accounts. On the first
poll where *every* provider succeeded, `SeedIconsOnFirstRun` fills `config.Icons` with what was
found. Adding a hardcoded default list is the wrong fix for "limit X is missing".

**Failures degrade, they do not blank the display.** A failed poll reuses the last good
readings (`RememberAndRestore`) and `PollScheduler` decides when that provider may be tried
again. `UsageCache` persists the last good readings to `cache.json` so a restart paints numbers
before the first poll returns.

`PollScheduler` is a separate, unit-tested class for a reason: **only cycles that actually
reached a provider may be recorded as failures.** An earlier version ran the skipped cycles
through the accounting too, so every tick pushed the deadline further out from "now" and the
provider was never polled again — invisible from the outside, and it looked exactly like a
rate limit that would not clear. `PollSchedulerTests.Waiting_out_a_pause_does_not_extend_it`
pins it down.

A rate limit (`ProviderResult.RateLimited`) takes the full pause on the first refusal instead
of climbing the ladder — retrying a spent quota only spends more of it.

**All colour comes from `Palette`.** Never a literal in a UI class — that is what keeps the
tray icon, hover card and details bar identical for the same value.

## Colour rules

Three rules, in priority order. Breaking any of them defeats the point of the system:

1. **Below the warn threshold**, a reading is drawn in its service's colour. That is what tells
   Claude icons from Codex icons.
2. **Limits of the same service are shaded apart** by lightness only (`ShadeSpread`), hue and
   saturation untouched, so three Claude icons differ without leaving the brand colour.
   Saturation is nudged up as lightness rises, or the last shade washes out to a pale tint.
3. **From the warn threshold on, severity wins** and shading stops. Every service, every limit
   shares one amber and one crimson — a warning must not depend on knowing the service palette.

Severity and neutral colours are *derived* from the two service colours: hue is set (amber,
crimson, near-grey), saturation and lightness come from their shared tone, so a re-tinted
palette stays coherent. `Harmony.Legible` then adjusts lightness for the taskbar theme without
touching hue.

## Constraints worth knowing before editing

- **`NotifyIcon.Text` is capped at 63 characters by WinForms** (not the 127 Win32 allows). The
  hover card exists partly to escape that; `TrayApp.Clamp` guards the fallback path.
- **Tray icon text must be drawn with GDI+ `Graphics.DrawString`, not `TextRenderer`.**
  TextRenderer uses GDI, which cannot composite onto transparency and leaves black fringes.
- **Do not set `StringAlignment.Center` in `TrayIconRenderer`.** The scale transform already
  centres the glyphs; doing both offsets the text by half its width and clips it (`17` renders
  as `/`). Alignment stays `Near`, and `TextRenderingHint` is `AntiAlias` — grid fitting
  distorts glyphs drawn through a transform.
- `Icon.FromHandle` does not own its handle: clone the icon, `DestroyIcon` the original, and
  dispose the previous `NotifyIcon.Icon` on every refresh, or each poll leaks a GDI object.
- Icon size comes from `SystemInformation.SmallIconSize` (24 px at 150 % scaling, not 16).
- **Size the fly-out from the target monitor's DPI** (`Native.DpiForPoint`), never the form's
  `DeviceDpi`: it is sized before being moved there, so on a mixed 4K/HD desktop its own DPI is
  still the previous monitor's. Fonts are in pixels for the same reason. `FlyoutPlacement` is
  WinForms-free precisely so every taskbar edge and screen size is unit-testable.
- The hover card must not take focus (`WS_EX_NOACTIVATE`), and there is no "mouse left the
  icon" event — it hides on an idle timer after `MouseMove` stops arriving.

## Localisation

`Localization/*.json`, globbed into the assembly and discovered at runtime, so a new language
is one file and no code. Embedded rather than .resx because satellite assemblies are not
bundled into a single-file publish.

Every user-facing string goes through `Loc.T`. Tests enforce that each language defines exactly
the English key set with matching `{0}` placeholders, so a translation mistake fails the build
rather than the app. `--once` output is deliberately always English, so pasted diagnostics stay
comparable.

## Auth handling

Both providers report an `AuthStatus` even when the poll failed — that is exactly when it
matters — and the details window renders it per service.

- Claude: `expiresAt` from the credentials file; access tokens last hours.
- Codex: the `exp` claim of the **`access_token`** JWT in `~/.codex/auth.json`. Ignore
  `id_token` — it expires after an hour and is not what requests are authorised with, so
  reading it would report a permanently expired login.

`claude.autoRefreshToken` is **off by default**: while Claude Code runs it keeps the token
fresh and the tray just re-reads it. That refresh path (`tokenUrl` + `clientId` in
settings.json) has not been exercised against the live endpoint — both are configurable so a
wrong value can be fixed without a rebuild, and failure degrades to "start Claude Code" rather
than touching the credentials file.

**Credentials only travel over https.** `Endpoint.IsSecure` refuses anything else before the
request is made, loopback excepted so a local mock stays possible — and the whole poll, token
refresh included, runs under one deadline, because the shared `HttpClient` has no timeout of its
own. A host other than the shipped one is *reported* through `ProviderResult.Notice` rather than
blocked: being able to follow a moved endpoint without a rebuild is the reason `usageUrl` and
`tokenUrl` are settings at all, so the answer to a foreign host is visibility, not a rule.
