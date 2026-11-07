# Contributing

Thanks for your interest. This is a tiny system-tray app and I'd like to keep it that way.

It does one thing: show Claude Code's live status on Windows. It stays local (the only network call is a daily update check), free (no API key, no spend), and small (a tray icon, not a dashboard).

It's a Windows port of the macOS [claude-status-bar](https://github.com/m1ckc3s/claude-status-bar), and the macOS source is the reference implementation: when in doubt about behavior, match it. Both apps read the same `state.json`, so its schema stays identical.

## What's welcome

Bug fixes, performance wins, animation and visual polish, and compatibility fixes (Windows 10/11 versions, x64/ARM64, terminals, high-DPI displays). New icon animations and styles are especially welcome.

Also check the [known issues](TROUBLESHOOTING.md#known-issues) in TROUBLESHOOTING.md: some behavior that looks like a bug is understood and intentional.

## Won't be merged

- Sending your conversation, files, or project to any API or relay.
- Anything that costs money or needs an API key.
- Usage meters, cost dashboards, analytics, or telemetry.
- Heavy work in the hooks. They run on every event, so they write one small state file and exit: no network, no per-prompt API calls.
- Hardcoding for one locale, provider, relay, or terminal.
- New settings stores or dependencies for a minor feature when what's already there works (prefs live in `app-settings.json`).
- Changing how your machine behaves: preventing sleep, running elevated helpers, services, or any background action beyond showing status. The app displays state, it doesn't act on your system.
- Codex support or support for other agents. Great projects, but as your own fork.

## Building

You'll need Windows 10/11, the .NET 8 SDK, and Node.js on PATH (the hooks run on Node).

```powershell
./build.ps1
```

Run the build through the .NET host (`dotnet ClaudeStatusBar.dll`), start a Claude Code session, and the icon appears in the tray (check the `^` overflow).

**Smart App Control:** freshly built dlls and test dlls are unsigned, so Windows Smart App Control may block them. If a build output or test run is blocked, retry, or switch Smart App Control to evaluation or off on your dev machine.

Build off the latest `main` so you're not fixing something that already changed.

## Code style

See [CLAUDE.md](CLAUDE.md). In short: small, single-purpose files; short comments that explain *why*, not *what*; keep `state.json`'s schema identical to macOS; and don't "simplify" away the tricky bits it lists (single-instance mutex, self-quit debounce, transcript recovery).

## Testing

Before you open a PR, actually run it. "Builds clean" is not testing.

```powershell
dotnet test tests/ClaudeStatusBar.Tests
```

```bash
# hooks: point USERPROFILE at a throwaway dir so your real ~/.claude is untouched
USERPROFILE=<tmp dir> node hooks/test/run-hook-tests.mjs
```

Then test it by hand on both surfaces, because they behave differently:

- **Claude Desktop** (Code tab), and
- the **CLI, in a terminal**.

Tell me which terminal you used (Windows Terminal, VS Code, and so on). For any visual or timing change, attach a screenshot or a short screen recording.

## What to expect

This is a hobby project. Replies can be slow, and I may decline a perfectly good PR because it adds complexity or scope I don't want to carry. That's not a knock on your work. When in doubt keep the change small, and check the [known issues](TROUBLESHOOTING.md#known-issues) first: timing, lifecycle, and self-quit especially are intentional and already understood.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/): `feat`, `fix`, `chore`, `refactor`, `style`, `docs`, `perf`. Branches: `type/kebab-case-description`.

## License

MIT. By contributing, you agree your contributions are licensed under it.
