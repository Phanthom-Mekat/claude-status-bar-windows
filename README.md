# Claude Status Bar for Windows

![Claude Status Bar for Windows](https://i.postimg.cc/KzDZ75WG/image.png)

A tiny Windows **system-tray** app that shows **Claude Code's live status**: an animated Claude icon while it's thinking or running a tool, a yellow dot when it's awaiting your permission, and the elapsed time of the current turn. Lightweight, no window by default, no taskbar clutter.

> Built so you can tab away during a long "thinking" stretch and still see, at a glance, whether Claude is working, waiting on you, or done.

> **Status:** in development.

---

## What it shows

- **Thinking / working** — the tray icon animates; hover for a live `1m 1s` timer.
- **Running a tool** — a short label (`Editing`, `Reading`, `Running command`, …) in the tooltip and menu.
- **Awaiting permission** — a paused yellow dot.
- **Idle / done** — rests on the Claude spark.

Optional extras (configurable from the right-click menu): an always-on-top **text pill** for always-readable status, a completion **chime**, the elapsed **timer**, **color** (Orange / System), and the **animation style** (Claude Spark / Claude Code / Crab Walking).

## Where it works

| Surface | Tracked? |
|---|---|
| Claude Code CLI (terminal) | ✅ |
| Claude Desktop — **Code** tab | ✅ (to verify on Windows) |
| Claude Desktop — **Chat** tab | ❌ |
| Cowork | ❌ |

## Requirements

- Windows 11 (Windows 10 likely works; not a v1 target)
- [Claude Code](https://claude.com/claude-code) (CLI or the Desktop app)
- Node.js (already required by Claude Code)

## Install

### Option A — One-line command (recommended)

In PowerShell:

```powershell
irm https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/install.ps1 | iex
```

This downloads the latest `ClaudeStatusBar.exe` from Releases, sets it to run at login, and launches it once (which installs the Claude Code hooks). Then **keep the icon always visible:** drag it from the `^` overflow into the tray, or **Settings → Personalization → Taskbar → Other system tray icons → toggle Claude Status Bar ON** (Windows 11 makes this a user choice). Start a Claude Code session and the icon reacts.

> SmartScreen may warn for an unsigned app → **More info → Run anyway**.

### Option B — Manual download

Download `ClaudeStatusBar.exe` from the [Releases](../../releases) page and run it once (it wires up the hooks and remembers its location).

### Option C — Claude Code plugin (hooks only)

```
/plugin marketplace add Phanthom-Mekat/claude-status-bar-windows
/plugin install claude-status-bar-windows@claude-status-bar-windows
```

The plugin installs the hooks but not the app — run `ClaudeStatusBar.exe` once so it can auto-launch later.

## How it works

The app is stateless. Claude Code hooks write the current status to `~/.claude/statusbar/state.json` (that's `C:\Users\<you>\.claude\statusbar\`); the app polls that file every 0.4 s and renders the icon. `SessionStart` launches it; it self-quits once Claude Desktop is closed and no Claude Code session is active. Its only network call is a once-a-day GitHub release check.

## Uninstall

One line:

```powershell
irm https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/uninstall.ps1 | iex
```

Or manually: `ClaudeStatusBar.exe --uninstall` (removes only this app's hooks from `settings.json`), then delete the exe and the `ClaudeStatusBar` run-at-login entry.

## Trademark / Not affiliated

This is an unofficial, open-source side project. **It is not affiliated with, endorsed by, or sponsored by Anthropic.** "Claude" and the Claude spark logo are trademarks of Anthropic, used here nominatively. MIT licensed — that covers the source code only and conveys no rights to Anthropic's trademarks or brand.

## License

MIT — see [LICENSE](LICENSE).
