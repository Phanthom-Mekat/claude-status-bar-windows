# Troubleshooting

**You don't open this app, it opens itself.** After install, the Claude Code hooks launch it whenever a session has activity, and it quits itself a few seconds after no Claude Code session is alive and Claude Desktop is closed. So starting it by hand with no session active can look like it launches and immediately quits. That is expected, not a crash: start a Claude Code session and the icon appears on its own.

**Installed while Claude Code sessions were already running?** Sessions already open appear the next time they do something (a prompt or a tool call), so the menu can look empty until then. Starting a new `claude` session also works.

**Chose Quit from the menu and it stays gone?** That's on purpose. Quit writes `~/.claude/statusbar/quit-intent`, so the hooks don't relaunch it mid-session. Starting a new Claude Code session clears it and the app comes back.

**Using Chat or Cowork in Claude Desktop?** Those don't move the icon. They don't fire the hooks this app runs on, so there's no live signal. Only Claude Code sessions do: Claude Desktop's Code tab, or `claude` in a terminal.

**Icon stuck on "working"?** After Esc, a denied permission, or a usage limit, Claude Code fires no hook to close the turn. The app recovers by reading the end of the session transcript, usually within a poll. If that misses, a safety net clears it after 15 minutes (2 hours while a permission prompt is pending). Sending a new prompt clears it right away.

**The icon doesn't appear at all?**
- **Check the `^` overflow.** Windows hides new tray icons there. To keep it visible:
  - Windows 11: drag it out of the overflow onto the taskbar, or **Settings → Personalization → Taskbar → Other system tray icons** and turn Claude Status Bar on.
  - Windows 10: right-click the taskbar → **Taskbar settings → Select which icons appear on the taskbar**.
  - Or turn on **Show floating pill** in the tray menu for an always-on-top status.
- Make sure a Claude Code session is actually running, not just a terminal window open.
- **Node.js on PATH?** The hooks run on Node: `node --version` must print a version. Claude Code on Windows runs hooks through Git Bash, so Node must be reachable from there too.
- **Smart App Control.** The app runs as `dotnet ClaudeStatusBar.dll` through the Microsoft-signed .NET host so Smart App Control allows it. Launching the unsigned `ClaudeStatusBar.exe` directly may be blocked. Re-running `install.ps1` sets this up correctly and installs the .NET 8 Desktop Runtime if it's missing.
- **Hooks can't find the app?** They relaunch it from the path in `~/.claude/statusbar/apppath.txt`. If that file is missing (for example, the plugin was installed but the app never ran), launch the app once by hand.

**Sessions that ended still listed?** A session is removed when its `claude.exe` process exits. If one lingers, it clears on the safety net above.

**Need logs for a bug report?** Set the environment variable `CLAUDE_STATUSBAR_DEBUG=1`, reproduce the problem, then look in `~/.claude/statusbar/app.log` (the app) and `~/.claude/statusbar/hooks.log` (the hooks).

**Uninstalling?** Run `uninstall.ps1` (see the [README](README.md#uninstall)), or `ClaudeStatusBar.exe --uninstall`, which removes only this app's hooks from `~/.claude/settings.json` and its scripts.

### What's in `~/.claude/statusbar/`

| File | What it is |
|---|---|
| `update.js`, `lifecycle.js` | The hook scripts |
| `sessions.d/` | One file per live session |
| `state.json` | Current status, written by the hooks, read by the app |
| `apppath.txt` | Path to the exe, so hooks can relaunch it |
| `apppid.txt` | The running app's process id |
| `app-settings.json` | Your menu preferences |
| `quit-intent` | Written by menu Quit; cleared by the next new session |
| `app.log`, `hooks.log` | Debug logs (only with `CLAUDE_STATUSBAR_DEBUG=1`) |

---

## Known issues

**Interrupting during the reasoning phase (CLI only) can freeze the status on a thinking word.** If you hit Ctrl+C while a turn is still in the extended-thinking phase, before any answer text has streamed, Claude Code writes nothing to the transcript and fires no hook, so there's no signal for the app to react to.
- Clears as soon as you send the next prompt in that session or close it.
- Interrupting *after* answer text starts streaming recovers normally, within a poll.
- Root cause is upstream in Claude Code (same as on macOS), not fixable from here.

---
Back to the [README](README.md).
