# Changelog

All notable changes to **Claude Status Bar for Windows** are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Releases are published on the [Releases](../../releases) page; the in-app updater checks them once a day.

## [0.6.0] — 2026-11-08

Brings the Windows app up to the macOS 0.4.5 feature set, plus a Windows-native menu, and adds a Linux build.

### Added
- **Linux.** The same app for Linux (x64 and arm64), with every feature in this release: tray icon and menu (KDE, GNOME with the AppIndicator extension, XFCE, Cinnamon, waybar), the floating pill with Sprout and a breathing glow, jump to a session, keep awake (desktop portal) and lid-closed mode (`systemd-inhibit`, same heat and idle safety rules). Install with one line: `curl -fsSL https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/linux/install.sh | sh`. See [Install on Linux](README.md#install-on-linux).
- **Keep PC awake while working** (on by default, idea from [Insomnia](https://github.com/krishhgg/Insomnia)). While any Claude session is thinking or running a tool, whether in VS Code, the CLI or Desktop, Windows won't idle-sleep. Once Claude finishes, your normal sleep timer starts over. A session waiting for your approval doesn't hold the PC awake. The screen can still turn off, and closing the lid still sleeps. No admin rights are needed.
- **Keep running with lid closed** (off by default). While Claude works, closing the lid doesn't sleep the PC, so you can shut the laptop and walk away. Safety rules: with the lid shut, it hibernates if the hottest sensor reaches 85 °C (change `LidHeatLimitC` in `app-settings.json`), and it also hibernates 30 s after Claude stops working, which is what Windows would have done when you closed it. Your own lid setting is saved before anything changes and comes back when Claude goes idle, when the app exits, at the next launch after a crash, and on uninstall. If the PC doesn't report a temperature, the mode stays off. No admin rights are needed.
- **One-click updates.** When the tray menu shows "⬆ Update to vX", clicking it downloads that release, checks it really is that version of the app, swaps it in, and restarts. You'll see an "Updated to vX" notice. It works while the app is running, because each old file is renamed aside rather than overwritten, and any failure rolls back and opens the download page instead.
- **Orbit animation style.** Three dots that drift together, orbit each other and settle back out, with the spark crossfading in and out, ported frame-for-frame from macOS.
- **Thinking words.** While Claude thinks, the status rotates through Claude Code's verbs (`Noodling…`, `Percolating…`) instead of a fixed `Thinking…`. A new word is picked each time a session starts thinking.
- **Completion Sound menu**: Off / Every turn / 1 min+ / 5 min+ / 15 min+, tracked per session. It plays the macOS chime (it used to be the Windows Asterisk sound). If you had the old "1m+" sound on, it carries over as 1 min+.
- **Click a session to jump to it.** For sessions in the Claude Code extension (VS Code, Cursor, Windsurf), it opens that exact conversation tab, even among several Claude tabs, and reopens it if the tab was closed. It focuses the right editor window first, then uses the extension's own `…://anthropic.claude-code/open?session=<id>` link. Other sessions bring their window to the front, restored if minimized: the terminal, or the Claude desktop app. Pinning a session to the status moved to **Ctrl+click**.
  - The first jump into an editor adds Claude Code to that editor's trusted link handlers (`extensions.confirmedUriHandlerExtensionIds`, the same thing as ticking "Do not ask me again"). This means no "Allow 'Claude Code' extension to open this URI?" prompt on every click. A notice says so, the settings file is backed up once, and it's never re-added if you remove it.
  - The app clears `ELECTRON_RUN_AS_NODE`, which it inherits from VS Code through the hooks. With it set, the `vscode://` link launched VS Code as plain Node (exit code 9) and nothing happened. It would also have broken "Open Claude".
- **Git branch in session rows** (`myproj · feat/auth`), read straight from `.git/HEAD`. Worktrees are supported, and a detached HEAD shows a short SHA. Same-named projects are told apart by their parent folder (`work/myproj` vs `tmp/myproj`), and hovering a row shows the full title, branch and path.
- **Sprout, the pill mascot.** A little orange companion with a spark antenna lives on the floating pill and acts out what Claude is doing: asleep when idle, gazing up with thought bubbles while thinking, bobbing while it works, wide-eyed with an amber "!" when it needs your approval, and hopping with sparkles when a turn finishes. It's drawn in code, so it stays crisp at any display scale. It's on by default; turn it off with **Pill mascot**.
- **"Done · 3m 12s".** When a turn finishes, the pill and menu say so, with how long it took, for a few seconds before going back to idle.
- **The status names what's being worked on**: `Editing TrayController.cs`, `Reading README.md`, or for shell commands, Claude's own description of the command (`Build the app`). On the floating pill, a long status ends in "…" (shorter still when two sessions show their project name), and hovering the pill shows the full text.
- **Show text** toggle for an icon-only floating pill.
- **The floating pill is always on at start.** Turning off "Show floating pill" hides it for the current run only, and it comes back the next time the app starts (that is, your next session). A "hidden" choice saved by an older version is ignored too.
- **CLI / APP tag** and a live status icon on every session row.
- New app icon (the Orbit dots).

### Changed
- **Redesigned tray menu.** It follows your Windows light/dark theme, with Windows 11 rounded corners, rounded hover highlights, brand-colored checkmarks and dim right-aligned detail text. Animation, Color and Completion Sound are now submenus that name your current choice.
- Animation styles are renamed **Spark**, **Unicode** and **Clawd™** (they were Claude Spark, Claude Code and Crab Walking). Your saved choice carries over.
- The floating pill grows away from the nearest screen edge, so a pill parked by the clock never slides off-screen.
- **Lower CPU.** Icon frames are rendered once and cached instead of rebuilt every frame. Session files and the transcript tail are only re-read when they change. The Claude Desktop process scan runs every few seconds instead of every poll, and the tray icon and tooltip are only pushed to the shell when they actually change.

### Fixed
- **Quit now sticks.** The hooks used to relaunch the app on your very next prompt. Quit now holds until you start a new Claude Code session or open the app yourself.
- **Sessions vanish when their terminal closes.** A session is now removed as soon as its `claude.exe` exits; it used to linger for up to 6 hours when no SessionEnd fired. On Windows, hooks run under Git Bash, so a hook's parent is a short-lived `bash.exe`. The hooks therefore ask the app once per session (`--claude-pid`) to walk the process tree to the real `claude.exe`.
- **Installing mid-session self-heals on any activity.** Any hook event (not only a new prompt) relaunches the app if it isn't running. The running check is now a cheap pid probe instead of spawning `tasklist`.
- A pending permission prompt no longer falls back to idle after 15 minutes. It now waits up to 2 hours.
- Desktop-app conversations you merely clicked through no longer clutter the Sessions list until you actually work in them.
- A brand-new session could inherit another session's state through the shared `state.json` fallback.

## [0.5.1] — 2026-06-28

### Fixed
- **Tray icon vanished moments after a first-time install.** A fresh install has no session marker, no `state.json`, and CLI-only users have no Claude Desktop running, so the "nobody needs me" self-quit fired ~8 s after install — the icon disappeared before the user could pin it (and before the "drag me to the tray" tip even finished showing), making the install look broken. The very first run now gets a 180 s grace before self-quit can fire; normal on-demand behavior resumes afterward.

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

[0.6.0]: ../../releases/tag/v0.6.0
[0.5.1]: ../../releases/tag/v0.5.1
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
