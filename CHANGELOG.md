# Changelog

All notable changes to **Claude Status Bar for Windows** are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Releases are published on the [Releases](../../releases) page; the in-app updater checks them once a day.

## [0.5.0] — 2026-06-27

### Added
- **State-color ring on the pill** — the overlay's outline encodes the state at a glance: orange while working, amber while awaiting permission, faint grey when idle.
- **Hover affordances** — the close `×` now appears only on hover, and the pill lifts (brightens) while hovered.

### Changed
- **Timer uses Cascadia Mono** in a fixed-width slot, so the ticking elapsed time no longer reflows/resizes the pill every second (falls back to Cascadia Code → Consolas).
- **The pill's `×` is now a transient dismiss** — it hides the overlay for the current run only and the overlay returns on next launch. The **Show text overlay** menu item remains the permanent off-switch.

### Fixed
- **Stuck "working" status on usage-limit hits.** When Claude hits a usage/session limit mid-turn, no `Stop` hook (and no usable `Notification`) fires, so the status used to stay animated until the 900 s safety net. The transcript check now also recognizes a limit abort (an `isApiErrorMessage` entry mentioning "limit") and clears to idle within one poll. The `isApiErrorMessage` gate keeps it precise — ordinary text mentioning "limit" is ignored.
- **Pill geometry** — the overlay is now shaped to its full client height (a true stadium), fixing a clipped bottom border and text that sat too low (the rounded shape was previously capped at 24 px and top-anchored).

## [0.4.0] — 2026-06-26

### Added
- **Universal install via the .NET host** — ships a framework-dependent bundle, and the installer auto-installs the .NET 8 Desktop Runtime if missing, so the app runs under Windows Smart App Control with no code-signing.

## [0.3.9] — 2026-06-26

### Changed
- **Runs under Windows Smart App Control** — launches through the Microsoft-signed `.NET` host (the `.dll`), with a pid-based running check, instead of the unsigned self-contained exe.

## [0.3.8] — 2026-06-26

### Changed
- Session/tab names are kept in the **Sessions** menu only, not on the main status text.

## [0.3.7] — 2026-06-26

### Added
- Sessions are shown by their **conversation title** (the first user message) in both the menu and the pill.

## [0.3.5] — 2026-06-26

### Added
- **Smart pin** — the status follows the selected session while it's active, and auto-shows whichever session is busy when the pinned one is idle.
- README multi-session section.

## [0.3.4] — 2026-06-26

### Added
- Click a session in the menu to **pin it to the status**; live-ticking timers in the menu.

### Changed
- A session row now pins to the status instead of opening its project folder.

## [0.3.3] — 2026-06-26

### Added
- **Sessions list** in the tray menu, with clickable rows that open the project folder; per-session `cwd` is stored.

## [0.3.2] — 2026-06-26

### Added
- "Update available" indication in the tray menu.

### Fixed
- Context menu not closing on click-outside.

## [0.3.1] — 2026-06-26

### Added
- **arm64 release** and an arch-aware installer.

### Changed
- The pill sits just above the taskbar so it stays always visible.
- The app stays alive on recent activity (so it doesn't self-quit mid-turn).

## [0.3.0] — 2026-06-26

Initial Windows port of the macOS [Claude Status Bar](https://github.com/m1ckc3s/claude-status-bar).

### Added
- System-tray app showing Claude Code's live status: animated icon while thinking/running a tool, a yellow dot while awaiting permission, and a resting spark when idle/done.
- Tooltip + right-click menu header with the live label and elapsed timer.
- Optional always-on-top **text pill** for always-readable status.
- Multi-session aggregation — tracks each session independently and shows the busiest.
- Hook installer that merges the Claude Code hooks into `~/.claude/settings.json` (with a one-time backup), plus a stateless `state.json` poll loop.
- Configurable completion chime, timer, color (Orange / System), and animation style (Claude Spark / Claude Code / Crab Walking).

[0.5.0]: ../../releases/tag/v0.5.0
[0.4.0]: ../../releases/tag/v0.4.0
[0.3.9]: ../../releases/tag/v0.3.9
[0.3.8]: ../../releases/tag/v0.3.8
[0.3.7]: ../../releases/tag/v0.3.7
[0.3.5]: ../../releases/tag/v0.3.5
[0.3.4]: ../../releases/tag/v0.3.4
[0.3.3]: ../../releases/tag/v0.3.3
[0.3.2]: ../../releases/tag/v0.3.2
[0.3.1]: ../../releases/tag/v0.3.1
[0.3.0]: ../../releases/tag/v0.3.0
