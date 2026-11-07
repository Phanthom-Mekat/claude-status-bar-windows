<!-- This app does one thing: show Claude Code's live status in the Windows system tray. Keep PRs small, focused, and tested. -->

## What this changes


## Issue
<!-- Link the issue this addresses if there is one (bug fixes and known-issue PRs are welcome without one). -->

## How you tested it
Behavior differs by surface and by terminal, so this is the part I actually read. Be specific about what you did and what you saw. "Builds clean" is not testing.

- [ ] `dotnet test tests/ClaudeStatusBar.Tests` passes
- [ ] If I touched `hooks/`: `node hooks/test/run-hook-tests.mjs` passes (with `USERPROFILE` set to a temp dir)
- [ ] Tested in **Claude Desktop (Code tab)**
- [ ] Tested in the **CLI, in a terminal**
- **Which terminal did you use?** <!-- Windows Terminal / VS Code terminal / other. Results can differ, so name it. -->
- **Windows version:** <!-- e.g. Windows 11 24H2 -->
- **What you did and what you saw:**

## Checklist
- [ ] I built off the latest `main`, so I'm not fixing something that already changed.
- [ ] One focused change, not a bundle of unrelated edits.
- [ ] Screenshot or short screen recording attached for any visual or timing change.
- [ ] The `state.json` schema is unchanged (both this app and the macOS app read it).
- [ ] I read CONTRIBUTING.md and the [known issues](https://github.com/Phanthom-Mekat/claude-status-bar-windows/blob/main/TROUBLESHOOTING.md#known-issues), and this fits the scope.

## Is this for everyone, or is it your fork?
<!-- Before submitting, ask whether this change is for this app's users or really belongs in your own fork. Codex support and other agents are out of scope here. -->
