# Contributing

Thanks for taking a look. Issues and pull requests are welcome.

Worth knowing up front: this codebase was written by Claude Code under human direction and has
had no independent human review — see *Provenance* in the README. Review it the way you would
review any unfamiliar code, and do not assume a comment is right just because it sounds
confident.

## Getting set up

You need the .NET 9 SDK and Windows — the app is WinForms and GDI+ throughout, so it does not
build or run anywhere else.

```powershell
dotnet build RateTray.sln
dotnet test tests\RateTray.Tests    # unit tests
dotnet test tests\RateTray.E2E      # launches the real .exe
```

The E2E tests skip themselves when there is no interactive desktop, and the ones that hit the
live APIs skip when the machine is not signed in to the CLIs. A green run on a headless machine
therefore proves less than a green run on yours — say which you ran.

**The Claude usage endpoint rate-limits.** The diagnostics tests deliberately share a single
`--once` invocation through a class fixture; one call per test was enough to earn a 429 in a
normal run. Keep it that way, and expect a 429 if you also poll by hand while iterating — the
app backs off on its own and recovers.

Useful while developing:

```powershell
dotnet run --project src\RateTray -- --details    # just the fly-out
dotnet run --project src\RateTray -- --settings   # just the settings dialog
```

`--once` prints every limit id your account exposes; attach its output to bug reports.

## Adding a language

One file, no code:

1. Copy `src/RateTray/Localization/en.json` to `xx.json` (ISO 639-1 code).
2. Translate the values. Leave the keys alone.
3. Build. The language appears under *right-click → Language* automatically — the files are
   globbed into the assembly and discovered at runtime.

Two rules the test suite enforces, so a mistake fails the build rather than the app:

- **Every key from `en.json` must be present**, and no extra keys.
- **Placeholders must match.** If English has `{0}` and `{1}`, your translation needs both.
  Dropping one loses data at runtime; inventing `{2}` throws.

`format.dateTime` is a .NET format string, not a sentence — adjust it to what your language
expects (`ddd dd.MM. HH:mm` vs `ddd MMM d, HH:mm`).

Note that `--once` output is deliberately always English, so bug reports stay comparable.

## Code conventions

`.editorconfig` covers formatting. Beyond that, the things reviewers will actually mention:

- **Comments explain why, not what.** The codebase has few comments and they earn their place:
  a Win32 constraint, a protocol quirk, a non-obvious ordering. If a comment restates the code,
  drop it.
- **Never hardcode which limits exist.** Which windows an account has depends on the plan.
  They are discovered at runtime; a fixed list is the wrong fix for "limit X is missing".
- **Colours go through `Palette`**, never as literals in a UI class. That is what keeps the tray
  icon, hover card and details bar the same colour for the same value.
- **User-facing strings go through `Loc.T`.** A literal string in the UI is a bug in two
  languages at once.

## Things that will bite you

Written down because each cost real time:

- `NotifyIcon.Text` is capped at **63 characters by WinForms**, not the 127 Win32 allows.
- Tray icon text must be drawn with GDI+ `Graphics.DrawString`, **not `TextRenderer`** — the
  latter uses GDI, which cannot composite onto transparency and leaves black fringes.
- Do not add `StringAlignment.Center` in `TrayIconRenderer`. The scale transform already
  centres the glyphs; doing both clips the text (`17` renders as `/`).
- `Icon.FromHandle` does not own its handle. Clone the icon, `DestroyIcon` the original, and
  dispose the previous `NotifyIcon.Icon` — otherwise every poll leaks a GDI object.
- `codex app-server` needs **stdin held open** until the reply arrives. Closing it makes the
  server exit before answering.
- Size the fly-out from the **target monitor's** DPI, not the form's `DeviceDpi`. On a mixed
  4K/HD desktop the form's own DPI is still the previous monitor's at that point.

## Pull requests

- One topic per PR.
- Add or adjust tests for behaviour you change. Provider parsing, colour derivation, placement
  and localisation are all unit-testable without a desktop — please use that.
- Say what you tested manually, especially for anything the automated tests cannot reach
  (tray rendering, hover cards, multi-monitor placement).

## Scope

This is a status indicator. Things that fit: more providers, more languages, better rendering,
accessibility. Things that do not: sending usage data anywhere, background agents, anything
requiring credentials the official CLIs do not already store locally.
