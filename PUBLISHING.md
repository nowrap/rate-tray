# Before making the repository public

A short checklist. Nothing here blocks the code from working — these are the things that only
become visible once the repo is on GitHub.

## Placeholders to replace

None left. Links written as `../../releases/latest` and `../../security/advisories/new` are
relative and resolve correctly once pushed.

## Optional: a CI badge

Add under the title in both READMEs once the repo exists:

```markdown
[![CI](https://github.com/OWNER/REPO/actions/workflows/ci.yml/badge.svg)](https://github.com/OWNER/REPO/actions/workflows/ci.yml)
```

## Repository settings worth setting

- **Description**: "Live usage limits for Claude Code and Codex, in the Windows tray."
- **Topics**: `windows`, `tray`, `dotnet`, `winforms`, `claude-code`, `codex`, `system-tray`
- **Security → Private vulnerability reporting**: enable it, or the link in `SECURITY.md`
  goes nowhere.
- **Actions → Workflow permissions**: the release workflow needs write access to contents.
  It requests this per-job, so the default read-only setting is fine.

## Releasing

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The release workflow runs the tests, publishes a single-file `win-x64` build, writes
`SHA256SUMS.txt` and creates the GitHub release. `dotnet publish` picks up the version from the
tag, so the tag must be `vMAJOR.MINOR.PATCH` — the workflow rejects anything else.

Move the entries under `## [Unreleased]` in `CHANGELOG.md` into the new version's section
before tagging.

## What is deliberately not in the repository

- `settings.json` and `cache.json` — user state, under `%APPDATA%\RateTray\`.
- Anthropic or OpenAI logos — trademarks. The service marks are generic shapes drawn in
  `ServiceBadge.cs`, and `README` says so explicitly under *Trademarks*.
- Real usage numbers. Test fixtures use the real payload *shape* with invented values.

## Worth knowing about the screenshots

`docs/details*.png` are rendered from invented readings by `tools/New-Screenshots.ps1`, not
captured from a running instance. A live capture would publish the account's subscription
tiers, its usage at that moment and its sign-in timestamps. Regenerate them after a visible UI
change:

```powershell
dotnet build -c Release
pwsh tools/New-Screenshots.ps1
```

They come out 840 px wide because the script runs on a 150 % display; the window is 560 logical
pixels.
