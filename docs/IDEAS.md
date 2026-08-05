# Ideas and open threads

Not a roadmap and not a set of promises — a place to keep the thinking that came out of
building 0.1.0, so it does not have to be reconstructed later. Anything here may turn out to be
a bad idea on closer inspection.

## Running somewhere other than Windows

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

### macOS is easier than Windows in one specific way

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

### The status line is the better fit for Linux

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

Extract `RateTray.Core` and add `--line`. Half a day, useful immediately on Windows for tmux
and starship users, and it turns "port the app" into three separate, tractable projects instead
of one large one. A side benefit: the core's unit tests would run on Linux CI runners, which
are faster and cheaper than the Windows ones.

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
self-contained build: no dependency, but roughly 70 MB instead of 345 KB. Declaring the
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

## Deliberately out of scope

Same line as [CONTRIBUTING.md](../CONTRIBUTING.md): this is a status indicator. Sending usage
data anywhere, running background agents, and anything requiring credentials the official CLIs
do not already store locally all stay out.
