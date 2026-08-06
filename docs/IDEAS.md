# Ideas and open threads

Not a roadmap and not a set of promises — a place to keep the thinking that came out of
building 0.1.0, so it does not have to be reconstructed later. Anything here may turn out to be
a bad idea on closer inspection.

## Running somewhere other than Windows

### Prior art: CodexBar already covers macOS and the Linux CLI

All of this turned up while researching *this* section — what a macOS or Linux port would take.
RateTray was already built by then, which is why none of it informed the decision to build it.

[steipete/CodexBar](https://github.com/steipete/CodexBar) is a macOS 14+ menu bar app doing the
same job for far more providers — Codex, Claude, Cursor, Gemini, Copilot, Bedrock, OpenRouter,
LiteLLM and others. MIT licensed, ~20k stars, actively maintained, and it ships a CLI with
macOS **and Linux** builds (Homebrew tap, Arch AUR `codexbar-cli`).

That settles the cross-platform directions below, which are kept only for the reasoning:

- **A macOS shell is not worth building.** Reimplementing a mature MIT project with broader
  coverage would help nobody.
- **The Linux status-line idea is largely served too.** `codexbar-cli` is exactly the "print one
  short line" command that tmux, starship and waybar want.

**Windows is covered too**, which was missed when this section was first written and is
corrected here rather than quietly deleted: [Win-CodexBar](https://github.com/nesszer/Win-CodexBar)
is a Windows tray app in Rust and Tauri, 56 providers, browser cookie import, DPAPI credential
storage, an installer, a winget package, and roughly 900 stars. The claim that RateTray existed
because no Windows counterpart did was simply wrong, and it had been published.

What is actually different is the approach, not the platform. Those are the deluxe version:
dashboards, dozens of providers, panels to open. RateTray keeps Core Temp's idea — the value is
drawn into the tray icon itself, so nothing has to be opened or clicked. 352 KB against 28 MB,
two services against 56. Whether that is worth having is a fair question; it is at least a
different question from "does anything else exist".

Being MIT, it is also a legitimate reference for how other providers expose their limits, if
this ever grows beyond two.

A second one exists — [Naruse0208/codex-rate-tray](https://github.com/Naruse0208/codex-rate-tray),
also C#, Codex only. Noted for completeness rather than as an alternative to point people at: it
carries **no licence**, which means all rights reserved by default, so nobody can legally reuse
or fork it.

### What already ports

The split is cleaner than it looks, because the providers were written without any knowledge of
the UI:

| Portable as-is | Windows-bound |
|---|---|
| `Providers/*` — Claude, Codex, `PollScheduler` | `TrayApp`, `DetailsForm`, `SettingsForm`, `TooltipWindow` (WinForms) |
| `Configuration/*` — settings and cache | `TrayIconRenderer`, `ServiceBadge`, `AppIcon` (GDI+) |
| `Model/LimitReading` | `AutoStart` (registry) |
| `Localization/Loc` | `Native` (P/Invoke) |
| `Ui/Harmony`, `Ui/Palette`, `Ui/FlyoutPlacement` | |

The last row surprises people: `Color`, `Rectangle` and `Point` live in
**System.Drawing.Primitives**, which is cross-platform. Only `Bitmap`, `Graphics` and `Icon`
come from System.Drawing.Common, which has been Windows-only since .NET 6. Colour derivation
and placement logic — and their unit tests — move unchanged.

Roughly two thirds of the value (fetching, backoff, caching, colour system, translations)
depends on no platform at all.

### macOS is easier than Windows in one specific way — but see the prior art above

`NSStatusItem` can put **text** in the menu bar directly. `TrayIconRenderer` — the single most
delicate file in the project, source of both the double-centring bug and the HICON leak —
disappears entirely. Set a string and a foreground colour; the colour rules still apply through
`NSAttributedString`.

What a macOS shell still needs: the menu-bar item, a popover for the details view, a settings
window, and a LaunchAgent instead of the registry Run key.

### A Linux tray would be the fragile part

Tray on Linux means StatusNotifierItem over D-Bus. KDE and XFCE cooperate, GNOME needs an
extension, and text in the tray is supported inconsistently across panels. That is permanent
fragility in exchange for a feature that only some users can see — worth doing last, if at all.

### The status line is the better fit for Linux — and `codexbar-cli` already provides it

On a headless or remote machine there is no tray, but there are four places this information
belongs:

- **tmux `status-right`** — runs a command periodically; exactly the shape of the problem
- **shell prompt** — a starship custom module, or plain `PS1`
- **waybar / polybar** — a custom module fed with JSON
- **a `watch`-style full view** for the occasional closer look

All four want the same thing: a command that prints one short line.

```console
$ ratetray --line
CL 24%  CX 6%

$ ratetray --line --format=waybar
{"text":"CL 24% CX 6%","tooltip":"Week · All models 24%…","class":"normal"}
```

Over SSH the constraint to remember is that **credentials live wherever the CLIs are signed
in**. If Claude Code runs on the remote box, this has to run there too and send only its output
back — which a CLI does naturally.

### Suggested first step

Extract `RateTray.Core` and add `--line`. Half a day. The cross-platform argument is weaker now
that `codexbar-cli` exists, but two reasons survive on their own: Windows users of tmux, WSL
and starship have no equivalent, and the core's unit tests would then run on Linux CI runners,
which are faster and cheaper than the Windows ones.

## Packaging

### winget

winget is a catalogue, not an integration: publication means opening a pull request against
[`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs) with three YAML files under
`manifests/n/nowrap/RateTray/<version>/`.

```yaml
# nowrap.RateTray.yaml
PackageIdentifier: nowrap.RateTray
PackageVersion: 0.1.0
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
```

```yaml
# nowrap.RateTray.installer.yaml
PackageIdentifier: nowrap.RateTray
PackageVersion: 0.1.0
InstallerType: portable
Commands:
  - ratetray
Dependencies:
  PackageDependencies:
    - PackageIdentifier: Microsoft.DotNet.DesktopRuntime.9
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/nowrap/rate-tray/releases/download/v0.1.0/RateTray.exe
    InstallerSha256: CDE245A0EE91B8B27210FACC749ACDF1D0E27F775767A28677293C637C453316
ManifestType: installer
ManifestVersion: 1.6.0
```

```yaml
# nowrap.RateTray.locale.en-US.yaml
PackageIdentifier: nowrap.RateTray
PackageVersion: 0.1.0
PackageLocale: en-US
Publisher: nowrap
PublisherUrl: https://github.com/nowrap
PackageName: RateTray
PackageUrl: https://github.com/nowrap/rate-tray
Moniker: ratetray
License: MIT
LicenseUrl: https://github.com/nowrap/rate-tray/blob/main/LICENSE
ShortDescription: Live usage limits for Claude Code and Codex, in the Windows tray.
Tags: [tray, claude, codex, rate-limit]
ManifestType: defaultLocale
ManifestVersion: 1.6.0
```

The checksum above is the real one from the 0.1.0 release, so these are close to submittable.

**Two decisions are baked into that.**

`InstallerType: portable` fits a single self-contained executable with no installer. winget puts
a shim in `%LOCALAPPDATA%\Microsoft\WinGet\Links` and onto `PATH`. The consequence worth stating
plainly: **no Start Menu entry.** The flow becomes `winget install ratetray`, run `ratetray`
once, then tick *Start with Windows* in the context menu. Acceptable for a tray tool, but not
the comfort of an MSI.

The **runtime dependency** matters more. The published build is framework-dependent, so without
the .NET 9 Desktop Runtime the executable fails to start. Declaring
`Microsoft.DotNet.DesktopRuntime.9` as above makes winget install it. The alternative is a
self-contained build: no dependency, but roughly 70 MB instead of 352 KB. Declaring the
dependency is what winget resolution exists for.

Test locally before submitting anything:

```powershell
winget settings --enable LocalManifestFiles   # once, as administrator
winget validate --manifest .\packaging\winget\
winget install --manifest .\packaging\winget\
```

From the second version on, submission can be automated in `release.yml`:

```yaml
      - name: Submit to winget
        shell: pwsh
        run: |
          curl.exe -L -o wingetcreate.exe https://aka.ms/wingetcreate/latest
          .\wingetcreate.exe update nowrap.RateTray `
            --version ${{ steps.version.outputs.version }} `
            --urls "https://github.com/nowrap/rate-tray/releases/download/${{ steps.version.outputs.tag }}/RateTray.exe" `
            --submit --token ${{ secrets.WINGET_TOKEN }}
```

`WINGET_TOKEN` needs to be a PAT with `public_repo`, so the tool can fork winget-pkgs and open
the pull request.

**The first submission is manual and reviewed by a human**, typically taking a few days. Only
afterwards does the automation apply.

### An MSI, if the portable install proves too thin

WiX would give a Start Menu shortcut, proper uninstall, and optionally autostart at install
time. It costs a toolchain in the build and really only pays off together with code signing.

### Code signing

The executable is unsigned. winget does not require it, but SmartScreen warns on first run —
particularly unfortunate for a tool that reads credential files. A certificate costs money
annually; the alternative is to wait until download reputation accumulates. Worth deciding
before submitting to winget rather than after.

## Forecasting how long the remainder lasts

The app polls every 90 s and throws each reading away once it has drawn it. Keeping them turns
"you are at 81 %" into "at this pace you run out before the window resets", which is the question
behind the number. Worth doing, but the honest version is harder than the arithmetic suggests.

**The arithmetic is the easy part.** Fit a slope over a trailing window of samples, then
`(100 − current) / slope` gives time-to-empty. Compare that against `ResetsAt`, which the app
already has. The useful output is not an absolute date, it is the comparison: *runs out ~2 h
before reset* or *comfortably clears it*.

**Burstiness is the real problem.** Agent work is spiky and idle-dominated. A slope measured
across twenty minutes of heavy use extrapolates to "empty in 40 minutes"; the same slope measured
across lunch says "never". A naive linear fit is therefore either alarmist or useless, and both
destroy trust in the number quickly. Two mitigations worth trying:

- Measure the rate over *active* samples only — those where the value actually moved — and label
  the result as such. "At the pace of your last hour of work" is a claim a user can check.
- Report the estimate as a range from the active and the wall-clock rate rather than one number.
  A forecast that admits its spread is more useful than a precise-looking one that is wrong.

**Gaps must not read as zero usage.** The machine sleeps, the app is closed, backoff pauses a
provider for up to 15 minutes. If a gap is treated as a sample of "no consumption", every
overnight break flattens the slope and the forecast says "plenty left" each morning. Gaps are
unknown, and a series with a large one should suppress the forecast rather than guess through it.

**Resets segment the series.** The percentage drops to zero at every reset, which a slope fit
would read as enormous negative usage. Samples have to be split at each `ResetsAt` boundary, and
only the current segment counts.

**Persistence is the part to be careful about.** History on disk changes what this tool *is*.
"Reads two files, contacts one endpoint by default, keeps nothing" is currently part of the pitch
and of [SECURITY.md](../SECURITY.md). A usage history is a record of when someone was working and how
hard — more revealing than the live number it is derived from. So: off by default, a documented
file path, a bound on the file's size, and a visible way to erase it.

**Start without persistence.** A first version that forecasts only from samples collected since
launch needs no new file, no settings migration, and no change to the privacy claim, and it
becomes useful within an hour of work — which is exactly when the question gets asked. Ship that,
see whether the number holds up against reality, and only then decide whether it is worth keeping
history across restarts.

Display would go in the details window as a line under each bar. It should not get a colour: the
three threshold rules are the whole colour vocabulary, and a forecast is an estimate, not a
reading. It does pair naturally with the existing notification threshold — "notify me when a
limit is *projected* to run out before its reset" is a better trigger than a fixed percentage.

## Loose ends from 0.1.0

- **`claude.autoRefreshToken` has never been exercised against the live endpoint.** It is off by
  default and documented as untested in [SECURITY.md](../SECURITY.md). Testing it means
  deliberately rotating a refresh token, which briefly disturbs the Claude Code sign-in on that
  machine.
- **No independent human code review.** See *Provenance* in the README.
- **The Claude usage endpoint's rate limit is undocumented.** A successful response carries no
  rate-limit headers at all, so there is no way to know the budget in advance. Whether a 429
  carries `Retry-After` is unknown — the code reads it if present and falls back to its own
  backoff. Finding out would mean deliberately provoking a 429.
- **More providers.** `IUsageProvider` was built for this; nothing else ships today. Whatever
  comes next needs a way to read limits that costs no model tokens, which is the part that took
  the longest for both existing providers.

## Reflecting the Windows overflow state in the Icons menu

Since 0.3.0 each tray icon has its own Windows identity (a GUID), so Windows remembers per icon
whether it sits on the taskbar or hidden behind the `^` overflow — the `IsPromoted` value under
`HKCU\Control Panel\NotifyIconSettings\<hash>`, keyed by the icon's `IconGuid`.

The in-app *Icons* submenu controls a different axis — whether RateTray draws an icon at all — so
the two can disagree: an icon that is enabled in RateTray but hidden by Windows looks, to someone
who only checked the app, like it is simply gone. Reading `IsPromoted` (read only) and annotating
such entries — "Session  (hidden in overflow)" — would close that gap.

Kept out of 0.3.0 on purpose:

- The store and its `IsPromoted`/`IconGuid` values are **undocumented**; a format change would
  quietly break the annotation. Read-only keeps the blast radius to a stale label.
- **Writing** `IsPromoted` — a "show this in the taskbar" button in the app — is the more tempting
  and more dangerous version: undocumented, and whether Explorer picks the change up without a
  restart is unverified. Promotion stays Windows' job.
- It is one-way and cosmetic. The real answer to "where did my icon go" is the note already in both
  READMEs about Windows 11 hiding new tray icons.

So: a small, read-only, opt-in nicety at most — worth it only if the overflow default trips users
up in practice.

## Deliberately out of scope

Same line as [CONTRIBUTING.md](../CONTRIBUTING.md): this is a status indicator. Sending usage
data anywhere, running background agents, and anything requiring credentials the official CLIs
do not already store locally all stay out.
