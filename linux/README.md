# Claude Status Bar for Linux

The Linux build of Claude Status Bar: a tray icon plus an optional always-on-top pill that show Claude Code's live status. It animates while Claude is thinking or running a tool, turns into an amber dot while it waits for your permission, and shows how long the current turn has been running.

It has every feature of the Windows app:

- the busiest session drives the icon
- the menu lists all sessions, and you can pin one
- tool labels, thinking words, git branch per session
- timer, completion chime
- Spark / Unicode / Clawd™ / Orbit animations
- Orange / System color
- icon-only pill
- **Sprout**, the pill mascot (asleep, pondering, working, asking for approval, celebrating), with a **Pill mascot** toggle
- "Done · 3m 12s" for a few seconds after a turn finishes
- one-click updates from the menu
- click a session to jump to it (its VS Code tab, or its terminal window; **Status follows** pins the status to one)
- keep PC awake while working, and keep running with the lid closed (heat and idle safety rules as on Windows)

The pill also glows: a soft orange halo while Claude works, and a brighter amber pulse when it's waiting on you.

## Install

```sh
curl -fsSL https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/linux/install.sh | sh
```

The full guide is in the main README under [Install on Linux](../README.md#install-on-linux): requirements, manual and from-source installs, enabling the tray on GNOME, checking that it works, and troubleshooting.

Uninstall: `sh ~/.local/share/claude-status-bar/uninstall.sh`.

## Where the icon shows up

| Desktop | Tray icon |
|---|---|
| KDE Plasma, Cinnamon, XFCE | ✅ built in (any panel that supports StatusNotifierItem tray icons works) |
| GNOME | ✅ with the AppIndicator extension (Ubuntu ships it on; elsewhere install "AppIndicator and KStatusNotifierItem Support") |
| Sway / Hyprland | ✅ in waybar's `tray` module |

The pill works everywhere. It's an X11 window, so on Wayland it runs through XWayland. Drag it to move it; left- or right-click it for the same menu as the tray icon.

## Build

```sh
sh linux/build.sh      # -> linux/dist/ClaudeStatusBar-linux-{x64,arm64}.tar.gz  (works from Git Bash on Windows too)
sh linux/test/e2e.sh   # end-to-end check in Docker: real hooks, X display, DBus tray protocol, screenshots in linux/test/out/
```

## How it's put together

- **Same model as Windows.** Hooks write `~/.claude/statusbar/sessions.d/<id>`, the app polls every 0.4 s, and the hook scripts are the same files (`../hooks`).
- **Logic is shared, not copied.** The csproj compiles the platform-neutral files from `../src/ClaudeStatusBar` directly (state parsing, session aggregation, turn tracking, git branch, settings, hook installer), so a fix there reaches Linux too.
- **Linux-specific files.**

  | File | What it does |
  |---|---|
  | `ProcessTree.cs` | finds the session's `claude` process via `/proc` |
  | `Chime.cs` | plays the chime through `pw-play` → `paplay` → `aplay` |
  | `Lifecycle.cs` | launch/quit rules (Linux has no Claude Desktop) |
  | `IconRenderer.cs` | draws the animation frames with Skia |
  | `TrayController.cs` | tray icon and menu, via Avalonia's StatusNotifierItem/DBusMenu |
  | `Pill.cs` | the floating pill |
  | `Mascot.cs` | Sprout, a line-for-line Skia port of the Windows `Mascot.cs` (keep the two in step) |
  | `Updater.cs` | one-click update: downloads the release tarball, verifies the version, swaps the install folder by rename, relaunches |
  | `SessionFocus.cs` | jump to a session: X11 `_NET_ACTIVE_WINDOW` on the window owned by its terminal/editor; `vscode://…/open?session=` for extension sessions |
  | `KeepAwake.cs` | idle-sleep inhibit through the desktop portal (`org.freedesktop.portal.Inhibit`), held on our own DBus connection |
  | `LidMode.cs` | lid-closed mode: a `systemd-inhibit` lock (handle-lid-switch + sleep) that dies with the app; thermal zones from `/sys/class/thermal`, lid from `/proc/acpi` |

- **Single instance** uses a locked file (`app.lock`), not a named mutex. The hooks start the app in a new login session, and .NET scopes named mutexes to the login session on Unix, so a mutex wouldn't stop a second copy.
