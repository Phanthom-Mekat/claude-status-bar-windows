# Claude Status Bar for Windows

![Claude Status Bar for Windows](https://i.postimg.cc/Hk2y6Q7D/Screenshot-2026-06-26-171821.png)

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

## Multiple sessions

Running several Claude Code sessions at once is fully supported. Each session is tracked independently, and the app keeps one calm, glanceable indicator instead of fighting over a single icon:

- **The icon follows the busiest session** — priority is **awaiting permission → working → thinking → idle** (so if one session needs your approval while another is editing, you see the yellow dot). The pill shows an **"N active"** count when more than one is running.
- **See them all in the right-click menu** — a **Sessions** list shows every live session with its project, state, and a live-ticking timer:

  ```
  Sessions
     myproj  ·  Editing · 1m 2s
     api     ·  Awaiting permission
     docs    ·  idle
  ```

- **Pin one to the status** — click a session in that list to make the icon and pill follow *that* session specifically (a ✓ marks it); click it again to go back to auto.

Under the hood each session writes its own state file and the app aggregates them, so the display stays accurate no matter how many sessions you have open.

## Requirements

- Windows 11 (Windows 10 likely works; not a v1 target)
- [Claude Code](https://claude.com/claude-code) (CLI or the Desktop app)
- Node.js (already required by Claude Code)
- .NET 8 Desktop Runtime — the installer adds it automatically if missing

## Install

### Option A — One-line command (recommended)

In PowerShell:

```powershell
irm https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/install.ps1 | iex
```

This installs the .NET 8 Desktop Runtime if needed (via `winget`), downloads the app, sets it to run at login, and launches it (which installs the Claude Code hooks). The app runs through Microsoft's **signed `.NET` host**, so it works **even under Windows Smart App Control** — no security block and no "unknown publisher" warning. Then **keep the icon always visible:** drag it from the `^` overflow into the tray, or **Settings → Personalization → Taskbar → Other system tray icons → toggle Claude Status Bar ON**. Start a Claude Code session and the icon reacts.

### Option B — Manual

Download `ClaudeStatusBar.zip` from the [Releases](../../releases) page, extract it (e.g. to `%LOCALAPPDATA%\ClaudeStatusBar`), and run it via the .NET host:

```powershell
dotnet "$env:LOCALAPPDATA\ClaudeStatusBar\ClaudeStatusBar.dll"
```

### Option C — Claude Code plugin (hooks only)

```
/plugin marketplace add Phanthom-Mekat/claude-status-bar-windows
/plugin install claude-status-bar-windows@claude-status-bar-windows
```

The plugin installs the hooks but not the app — run it once (Option A or B) so it can auto-launch later.

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
