# Claude Status Bar for Windows

![Claude Status Bar for Windows](https://i.postimg.cc/Hk2y6Q7D/Screenshot-2026-06-26-171821.png)

A tiny Windows **system-tray** app that shows **Claude Code's live status**: an animated Claude icon while it's thinking or running a tool, a yellow dot when it's awaiting your permission, and the elapsed time of the current turn. Lightweight, no window by default, no taskbar clutter.

> Built so you can tab away during a long "thinking" stretch and still see, at a glance, whether Claude is working, waiting on you, or done.

> **Also on Linux:** the same app, with every feature, runs on x64 and ARM64 Linux. See [Install on Linux](#install-on-linux).

> **Status:** in development.

---

## What it shows

- **Thinking / working** — the tray icon animates and the status rotates through Claude Code's playful thinking words (`Noodling…`, `Percolating…`); hover for a live `1m 1s` timer.
- **Running a tool** — what it's working on (`Editing TrayController.cs`, `Build the app`, …) in the tooltip, menu and pill.
- **Awaiting permission** — a paused yellow dot.
- **Done** — `Done · 3m 12s` for a few seconds, then it rests on the Claude spark.

Optional extras (configurable from the right-click menu, which follows your light/dark theme):

- an always-on-top **floating pill** for always-readable status, starring **Sprout**, a little mascot that sleeps, ponders, works, waves for your approval and celebrates along with Claude (turn **Show text** off for an icon-only pill),
- a **completion sound**: Off / Every turn / 1 min+ / 5 min+ / 15 min+,
- the elapsed **timer**, **color** (Orange / System),
- the **animation style**: Spark, Unicode, Clawd™, or **Orbit** (three dots that drift together, orbit each other, and settle back out),
- **keep awake** while Claude works, even with the **lid closed** (see [Keep your PC awake](#keep-your-pc-awake-while-claude-works)).

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
- **See them all in the right-click menu** — a **Sessions** list shows every live session: its conversation title, the project and **git branch**, a live status icon (animating while working, amber dot when it needs approval), a live-ticking timer, and whether it runs in a terminal/editor (**CLI**) or the desktop app (**APP**):

  ```
  Sessions
   ✻ fix the login bug   myproj · feat/auth          1m 2s · CLI
   ● add rate limits     api · main         needs approval · APP
   • docs · main                                           CLI
  ```

  Two sessions in same-named folders are told apart by their parent folder (`work/myproj` vs `tmp/myproj`); hover a row for the full path.
- **Jump to a session** — click it to go straight there. In VS Code / Cursor it opens that session's own Claude tab (in the right window, even with several Claude tabs open, and reopens it if you closed it). The first time, the app tells your editor to trust Claude Code's links, so you're not asked "Allow 'Claude Code' extension to open this URI?" on every click. To undo it, remove `anthropic.claude-code` from `extensions.confirmedUriHandlerExtensionIds` in your editor settings. For a terminal or the Claude desktop app it brings the window to the front; Windows has no way to pick a terminal's tab.
- **Pin one to the status** — **Ctrl+click** a session to make the icon and pill follow *that* session (it turns bold); Ctrl+click it again to go back to auto.
- **The list stays tidy** — a session disappears the moment its Claude Code process exits (closed terminal, crash), desktop-app conversations you merely clicked through stay hidden until you actually work in them, and rows idle for 15 minutes are tucked away.

Under the hood each session writes its own state file and the app aggregates them, so the display stays accurate no matter how many sessions you have open.

## Keep your PC awake while Claude works

Leave Claude on a long task and walk away. Your PC won't go to sleep halfway through. (The idea comes from [Insomnia](https://github.com/krishhgg/Insomnia) for macOS.) Both options are in the right-click menu and need no admin rights.

- **Keep PC awake while working** (on by default). While any session is thinking or running a tool, whether in VS Code, the terminal or the desktop app, Windows won't go to sleep from being idle. When Claude finishes, your normal sleep timer starts again from zero. A session waiting for your approval doesn't keep the PC awake, because nothing runs until you answer. The screen can still turn off, and Windows still locks as usual.
- **Keep running with lid closed** (off by default). While Claude works, you can shut the laptop and it keeps going. The app sets Windows' lid action to *Do nothing* while Claude works and puts your own setting back afterwards. With the lid shut, it watches the laptop:

  | With the lid shut | What happens |
  |---|---|
  | Claude is working and the laptop is cool | It keeps running. The screen is off. |
  | The hottest sensor reaches **85 °C** | It **hibernates** right away. Hibernating uses no power, so the laptop cools completely. Open the lid and everything is where you left it. |
  | Claude stops working for 30 s | It **hibernates**, which is what Windows would have done when you closed the lid. |

  Your lid setting is saved before anything changes. It comes back when Claude goes idle with the lid open, when the app quits, at the next launch after a crash, and when you uninstall. If your PC doesn't report a temperature, the mode stays off and tells you why.

> **Use it on a desk, not in a closed bag.** The heat cutoff is a safety net, not a guarantee. Before you rely on it, try it once while you watch: start a task, close the lid for a minute, then open it and check that Claude kept working.

**Change the heat limit:** set `"LidHeatLimitC"` in `%USERPROFILE%\.claude\statusbar\app-settings.json`. Sensors read differently on each laptop.

**Using the laptop closed with an external monitor?** Turn lid mode off. Otherwise the laptop hibernates when Claude finishes.

## Install on Windows

### Requirements

- Windows 11 (Windows 10 likely works; not a v1 target)
- [Claude Code](https://claude.com/claude-code) (CLI or the Desktop app)
- Node.js (already required by Claude Code)
- .NET 8 Desktop Runtime — the installer adds it automatically if missing

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

## Install on Linux

### Requirements

- x86-64 or ARM64 Linux with a graphical desktop (X11 or Wayland)
- [Claude Code](https://claude.com/claude-code) CLI
- Node.js on your `PATH` (Claude Code runs the status hooks with it)
- A desktop that can show tray icons. KDE Plasma, Cinnamon and XFCE show them out of the box. **GNOME** needs the AppIndicator extension (see [Show the tray icon on GNOME](#show-the-tray-icon-on-gnome)). On Sway or Hyprland, use waybar's `tray` module.

No .NET runtime is needed (the Linux build is self-contained), and nothing is installed system-wide, so you never need `sudo` for the app itself.

### Option A — One-line command (recommended)

```sh
curl -fsSL https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/linux/install.sh | sh
```

The script:

1. picks the right build for your CPU (x64 or ARM64),
2. downloads the latest release,
3. installs it to `~/.local/share/claude-status-bar`,
4. adds a `claude-status-bar` command to `~/.local/bin` and an entry to your app launcher,
5. starts the app.

On that first start, the app adds its hooks to `~/.claude/settings.json`. Your existing settings and hooks are kept, and a backup is saved as `~/.claude/settings.json.bak-statusbar`. From then on, Claude Code starts the app by itself whenever a session begins, and the app quits once no sessions are left. You don't need to set up autostart.

### Option B — Manual, from the release tarball

1. Check your CPU type with `uname -m`: `x86_64` means **x64**, `aarch64` means **arm64**.
2. Download, extract and install (for ARM64, replace `x64` with `arm64`):

   ```sh
   curl -LO https://github.com/Phanthom-Mekat/claude-status-bar-windows/releases/latest/download/ClaudeStatusBar-linux-x64.tar.gz
   mkdir claude-status-bar
   tar -xzf ClaudeStatusBar-linux-x64.tar.gz -C claude-status-bar
   sh claude-status-bar/install.sh
   ```

   `install.sh` installs from the folder it sits in, so nothing else is downloaded. You can delete the tarball and folder afterwards.

### Option C — Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0):

```sh
git clone https://github.com/Phanthom-Mekat/claude-status-bar-windows
cd claude-status-bar-windows
sh linux/build.sh      # builds linux/dist/ClaudeStatusBar-linux-{x64,arm64}.tar.gz
sh linux/install.sh    # installs the build that matches your CPU
```

### Show the tray icon on GNOME

GNOME hides tray icons unless the **AppIndicator** extension is on. Ubuntu ships it already enabled. Elsewhere:

| Distro | Install |
|---|---|
| Fedora | `sudo dnf install gnome-shell-extension-appindicator` |
| Debian | `sudo apt install gnome-shell-extension-appindicator` |
| Arch | `sudo pacman -S gnome-shell-extension-appindicator` |

Log out and back in, then turn it on:

```sh
gnome-extensions enable appindicatorsupport@rgcjonas.gmail.com
```

Even without a tray icon, the **floating pill** still shows the live status.

### Check that it works

1. Start `claude` in any terminal and give it a task.
2. The tray icon starts animating, and a floating pill appears near the bottom-right corner of your screen.
3. Right-click the tray icon, or click the pill, for the menu. It has the session list and the options (timer, text, pill, animation, color, completion sound).

To start the app by hand, run `claude-status-bar`. If `~/.local/bin` isn't on your `PATH`, run `~/.local/share/claude-status-bar/ClaudeStatusBar` instead.

### Linux notes

- **The pill** stays on top and can be dragged anywhere; it remembers where you put it. On Wayland it runs through XWayland.
- **The completion sound** plays through `pw-play` (PipeWire), `paplay` (PulseAudio) or `aplay` (ALSA), whichever you have installed.
- **Click a session in the menu to jump to it.** For a Claude Code extension session (VS Code, Cursor, Windsurf), the exact conversation tab opens. As on Windows, the first time the app tells your editor to trust Claude Code's links (in `~/.config/Code/User/settings.json`, or Cursor's/Windsurf's), so you're not asked "Allow 'Claude Code' extension to open this URI?" on every click; remove `anthropic.claude-code` from `extensions.confirmedUriHandlerExtensionIds` there to undo it. For a terminal session, its window comes to the front. On Wayland this works only for windows running through XWayland (VS Code does by default), because Wayland doesn't let one app raise another's window. Linux menus can't detect Ctrl+click, so to make the status follow one session, use **Status follows** in the menu instead.
- **Keep PC awake while working** (on by default) asks your desktop not to idle-suspend while Claude is thinking or running a tool. It uses the desktop portal (GNOME, KDE and others), the same request a video player makes. The screen can still turn off, and closing the lid still sleeps.
- **Keep running with lid closed** (off by default) stops the lid from suspending the laptop while Claude works. It uses a systemd inhibitor lock, so no root is needed, but it requires systemd. The safety rules match Windows: with the lid shut, the laptop hibernates (or suspends, if hibernation isn't set up) when the hottest sensor reaches 85 °C, and 30 s after Claude stops working. If the laptop doesn't report a temperature or a lid switch, the mode stays off. The lock belongs to a helper that exits with the app, so a crash can't leave your lid disabled.
- **No icon or pill?** Start the app with logging on:

  ```sh
  CLAUDE_STATUSBAR_DEBUG=1 claude-status-bar
  ```

  Then read `~/.claude/statusbar/app.log`. The app can't start in a session with no display, such as plain SSH. It exits quietly there, and the hooks keep working.
- **Updating:** when a new version is out, the menu shows **⬆ Update to vX**. Click it and the app downloads that release, checks it really is that version, swaps it in and restarts. If anything fails, nothing changes and the release page opens instead. Re-running the one-line command also installs the latest version.
- **Claude Desktop** has no official Linux build, so only Claude Code sessions are tracked. **Open Claude** in the menu opens the web app.

## How it works

The app is stateless. Claude Code hooks write each session's status to `~/.claude/statusbar/sessions.d/<session>` (that's `C:\Users\<you>\.claude\statusbar\` on Windows); the app polls those files every 0.4 s and renders the icon. Any session activity launches it if it isn't running; it self-quits once Claude Desktop is closed and no Claude Code session is alive. Choosing **Quit** from the menu sticks until you start a new session. Its only network calls are a once-a-day GitHub release check and, when you click **⬆ Update to vX** in the menu, downloading that release. That click installs the update and restarts the app; if anything goes wrong, nothing changes and the download page opens instead.

Something not working? See [TROUBLESHOOTING.md](TROUBLESHOOTING.md).

## Uninstall

**Windows**, one line:

```powershell
irm https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/uninstall.ps1 | iex
```

Or manually: `ClaudeStatusBar.exe --uninstall` (removes only this app's hooks from `settings.json`, and restores your lid setting if lid mode changed it), then delete the exe and the `ClaudeStatusBar` run-at-login entry.

**Linux**:

```sh
sh ~/.local/share/claude-status-bar/uninstall.sh
```

This removes only this app's hooks from `~/.claude/settings.json`, stops the app, and deletes its files, the `claude-status-bar` command and the launcher entry. Your preferences in `~/.claude/statusbar/app-settings.json` are kept.

## Trademark / Not affiliated

This is an unofficial, open-source side project. **It is not affiliated with, endorsed by, or sponsored by Anthropic.** "Claude" and the Claude spark logo are trademarks of Anthropic, used here nominatively. MIT licensed — that covers the source code only and conveys no rights to Anthropic's trademarks or brand.

## License

MIT — see [LICENSE](LICENSE).
