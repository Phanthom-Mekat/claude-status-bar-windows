#!/bin/sh
# Remove Claude Status Bar for Linux: strips our hooks from ~/.claude/settings.json, stops the app, deletes its files.
# Your prefs in ~/.claude/statusbar/app-settings.json are left in place.
set -u
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
DEST="$DATA/claude-status-bar"

echo "Uninstalling Claude Status Bar..."
pid=$(cat "$HOME/.claude/statusbar/apppid.txt" 2>/dev/null || true) # read first: --uninstall may delete it
[ -x "$DEST/ClaudeStatusBar" ] && "$DEST/ClaudeStatusBar" --uninstall
[ -n "$pid" ] && [ "$(cat "/proc/$pid/comm" 2>/dev/null)" = "ClaudeStatusBar" ] && kill "$pid"
rm -rf "$DEST"
rm -f "$HOME/.local/bin/claude-status-bar" "$DATA/applications/claude-status-bar.desktop"
echo "Removed. (Your ~/.claude/statusbar/app-settings.json prefs are left in place.)"
