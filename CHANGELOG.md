# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- The usage endpoint is asked at most once every five minutes, whatever the refresh interval is
  set to. A weekly window does not move in ninety seconds, and the tray is not the only client
  spending the account's request quota — so polling that bought nothing was earning the 429 that
  then froze the display for a quarter of an hour at a time. `claude.minIntervalSeconds` sets the
  floor and the settings dialog exposes it; the menu's refresh command ignores it, because
  someone asking for numbers now is entitled to the request. Codex is unaffected: it is a local
  process with nothing to spend.
- A Codex plan with a per-model quota says which model each limit belongs to. Two weekly windows
  both read "Codex · Week", so the model's own limit — sitting at 0 % until that model runs —
  looked like the account's limit had stopped filling. The model's window is now labelled after
  it, the way Claude's per-model window already was, and only the account-wide window keeps the
  "active" dot: whether a model is the one currently running is not something the server says.

## [0.3.1] - 2026-08-18

A patch release for three things that were wrong in plain sight: a limit that was full without
saying so, an error message that took the panel down with it, and a sign-in called valid while
the server was refusing it.

### Fixed

- A full limit shows **100** in its tray icon. It used to stop at 99, on the assumption that three
  digits do not fit — they do, the renderer scales them down — so the one reading that has to be
  right was the one disagreeing with the details window and the hover card.
- A failed poll no longer paints the server's answer across the panel. Codex hands the upstream HTTP
  body back verbatim, several lines of JSON, and every line after the first was drawn over the
  readings below it. An error is now a single line wherever it is shown, cut with an ellipsis where
  it does not fit — as is every other string the app does not write itself.
- A Codex sign-in the server refuses is reported as expired, instead of "valid until" a date days
  away. The access token in `auth.json` can be revoked long before the expiry it carries; when the
  server answers `token_expired`, that answer wins, and the error says to run `codex login`.

## [0.3.0] - 2026-08-07

Polish where it shows and reach where it counts. The tray numbers read cleaner, the hover card
behaves itself, and each icon is now a first-class Windows tray citizen you can show or hide on its
own — plus an About box with a built-in update check.

### Added

- An About dialog (right-click → *About RateTray*): version, copyright, a link to the repository, a
  nudge to star it, and a *Check for updates* button. It can also check once a day on start-up —
  **off by default, opt-in from the dialog** — and marks the About entry when a newer version
  exists. Either way the check only reads the repository's tag list from GitHub: no token, nothing
  about you, a short timeout, and silent on any failure.
- A stable Windows identity per icon. Each limit's icon registers through `Shell_NotifyIcon` with
  its own GUID, so Windows lists them as separate entries under *Other system tray icons* that can
  be shown or hidden individually — and the choice survives a restart. Until now every icon
  collapsed into one shared entry, the way WinForms registers them, so a single show/hide toggle hit
  them all. **After updating, the icons appear once behind the `^` overflow arrow** — Windows treats
  the new per-icon identities as new icons and hides them — until you drag them onto the taskbar
  again; from then on Windows remembers each one.

### Changed

- The tray numbers line up. Every value is scaled from one reference and centred on its actual
  painted pixels, so a one- and a two-digit number share a size and a baseline instead of each being
  fitted to its own box; they also sit a little more quietly, sized to leave a margin rather than
  fill the icon edge to edge.
- The hover card holds still. It no longer flickers while the pointer rests on an icon, anchors to
  the taskbar edge on the bar and to the top of the overflow flyout inside the `^` popup, and stays
  out of the way while the details window or the context menu is open.
- About and Settings are limited to one open dialog at a time; asking for one while the other is up
  brings the existing window forward rather than stacking a second.

### Fixed

- A second click on a tray icon closes the details window instead of reopening it — the click had
  been dismissing and immediately re-showing it — and a click inside the window dismisses it.

## [0.2.0] - 2026-08-06

Hardening at the edges the app does not control: a file someone edited by hand, a cache someone
changed, an endpoint that accepts a connection and then says nothing. Found by an independent
review of the 0.1.0 code, none of it reachable on the default path.

A minor version rather than a patch for one reason worth stating plainly: a configuration that
worked before can now fail on purpose. An endpoint set to plain `http` is refused instead of
being used, because that is a credential going out in clear.

### Fixed

- A hand-edited `settings.json` no longer decides whether the app starts. Values that are valid
  JSON but not valid settings — `"icons": null`, `"theme": null`, a refresh interval large enough
  to overflow the poll timer — are normalised on load, and the repaired file is written back.
- A cache entry that is valid JSON but unusable — `{"Claude": null}`, or one whose readings are
  null — is dropped instead of throwing during start-up. `UsageCache.Load` documents itself as
  never throwing; now that also holds for the file being semantically empty rather than malformed.
- The Claude token refresh runs under the same deadline as the usage request it precedes. It was
  given only the shutdown token, and the shared `HttpClient` has no timeout of its own, so a token
  endpoint that stopped responding blocked polling until the app was restarted — the manual
  refresh included, since it waits behind the same guard. Reachable only with
  `claude.autoRefreshToken` enabled, which is off by default.
- Restored readings age out. The two-day limit applied only when the cache was read from disk, so
  an app left running for days with one provider down kept showing numbers that a restart would
  have discarded.

### Changed

- The details window footer shows the oldest of the values on display instead of the newest. A
  provider polling normally could otherwise put its own timestamp under numbers another one had
  been serving from cache for hours.

### Security

- `SECURITY.md` claimed the Claude token goes to exactly one address. `claude.usageUrl` and
  `claude.tokenUrl` are settings, so that was true of the shipped configuration and not of the
  program. The section now says which it is, and what follows from it.
- A credential is only sent over https. Both URLs stay settings — correcting a moved endpoint
  without a rebuild is the reason they exist — but a plain-http value now fails the poll before a
  connection is opened, with loopback excepted so a local mock still works.
- An endpoint whose host is not the shipped one is named in the details window and in `--once`.
  Pointing the tray somewhere else stays a deliberate choice; it stops being an invisible one.

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

[Unreleased]: https://github.com/nowrap/rate-tray/compare/v0.3.1...HEAD
[0.3.1]: https://github.com/nowrap/rate-tray/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/nowrap/rate-tray/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/nowrap/rate-tray/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/nowrap/rate-tray/releases/tag/v0.1.0
