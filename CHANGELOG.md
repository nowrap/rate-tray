# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-08-05

First release.

### Added

- Tray icons rendering each usage limit as a number, Core Temp style, one `NotifyIcon` per limit.
- Claude limits via the OAuth usage endpoint, using the token Claude Code maintains on disk.
- Codex limits via `codex app-server` JSON-RPC (`account/rateLimits/read`) — live values at no
  token cost.
- Icon set discovered from the account on first run, so per-model windows such as Fable appear
  without being hardcoded.
- Details fly-out with a progress bar, reset time and sign-in validity per service, placed in
  the taskbar corner and sized from the target monitor's DPI.
- Hover card showing the service mark alongside the value, replacing the 63-character Windows
  tooltip.
- Settings dialog covering every option, with a live preview of the tray icons.
- Colour system: service colour below the warning threshold, severity colour above it, with
  warning, critical and neutral derived from the service colours in HSL. Limits of the same
  service are shaded apart by lightness; shading stops at the warning threshold so amber and
  crimson keep one meaning.
- Failed polls keep the last readings on screen and back that provider off exponentially,
  capped at 15 minutes, honouring a server-stated `Retry-After` when there is one; the menu's
  refresh command clears the backoff. A rate limit takes the full pause on the first refusal
  rather than climbing to it, since retrying a spent quota only spends more of it.
- Strip along the bottom of the details window counting down to the next refresh, and a
  "next attempt in …" note beside the error while a provider is backed off.
- Poll interval, backoff ceiling and per-service request timeouts are all configurable;
  `settings.json` gains any option a newer version adds, on the next start.
- Poll interval defaults to 90 s with a floor of 30 s — the windows being watched move in hours
  and days, and the usage endpoint rate-limits a tight loop.
- Readings cached to `cache.json`, so a restart shows numbers before the first poll returns.
- Application icon generated from the palette by `tools/New-AppIcon.ps1`.
- English and German, discovered from embedded JSON files; switchable at runtime.
- Threshold notifications, once per reset window.
- Autostart via the per-user Run key.
- `--once`, `--details` and `--settings` diagnostic modes.

[Unreleased]: https://github.com/nowrap/rate-tray/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/nowrap/rate-tray/releases/tag/v0.1.0
