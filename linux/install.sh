#!/bin/sh
# Install Claude Status Bar for Linux, per user (no sudo).
#
#   One line (downloads the latest release):
#     curl -fsSL https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/linux/install.sh | sh
#   From an extracted release tarball or a local build:
#     sh install.sh            (run inside the extracted folder, or next to dist/<rid>/)
#     sh install.sh <dir>      (a folder containing the ClaudeStatusBar binary)
set -eu

REPO="Phanthom-Mekat/claude-status-bar-windows"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
DEST="$DATA/claude-status-bar"
BIN="$HOME/.local/bin"

case "$(uname -m)" in
  x86_64|amd64) RID=linux-x64 ;;
  aarch64|arm64) RID=linux-arm64 ;;
  *) echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
esac

command -v node >/dev/null 2>&1 || echo "warning: 'node' isn't on PATH. Claude Code runs the status hooks with node." >&2

# Where to install from: an argument, this script's folder, a local build, or the latest GitHub release.
SRC="${1:-}"
if [ -z "$SRC" ]; then
  here=$(cd "$(dirname "$0")" 2>/dev/null && pwd || true)
  for c in "$here" "$here/dist/$RID"; do
    if [ -n "$here" ] && [ -f "$c/ClaudeStatusBar" ]; then SRC="$c"; break; fi
  done
fi
if [ -z "$SRC" ]; then
  TMP=$(mktemp -d)
  trap 'rm -rf "$TMP"' EXIT
  url="https://github.com/$REPO/releases/latest/download/ClaudeStatusBar-$RID.tar.gz"
  echo "Downloading $url"
  curl -fsSL "$url" | tar -xz -C "$TMP"
  SRC="$TMP"
fi
[ -f "$SRC/ClaudeStatusBar" ] || { echo "No ClaudeStatusBar binary in $SRC" >&2; exit 1; }

# Stop a running copy so its files can be replaced (only if that pid really is us: pids get reused).
pid=$(cat "$HOME/.claude/statusbar/apppid.txt" 2>/dev/null || true)
if [ -n "$pid" ] && [ "$(cat "/proc/$pid/comm" 2>/dev/null)" = "ClaudeStatusBar" ]; then kill "$pid"; sleep 1; fi

echo "Installing to $DEST"
rm -rf "$DEST"
mkdir -p "$DEST" "$BIN" "$DATA/applications"
cp -R "$SRC"/. "$DEST"/
chmod +x "$DEST/ClaudeStatusBar"
ln -sf "$DEST/ClaudeStatusBar" "$BIN/claude-status-bar"

cat > "$DATA/applications/claude-status-bar.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Claude Status Bar
Comment=Live Claude Code status in your system tray
Exec=$DEST/ClaudeStatusBar
Icon=$DEST/claude-status-bar.png
Terminal=false
Categories=Development;Utility;
EOF

# First launch installs the hooks into ~/.claude/settings.json (backup: settings.json.bak-statusbar).
# After that, Claude Code's SessionStart hook launches the app on demand.
rm -f "$HOME/.claude/statusbar/quit-intent"
nohup "$DEST/ClaudeStatusBar" >/dev/null 2>&1 &

echo
echo "Done. Claude Status Bar is running in your system tray."
echo "  GNOME: tray icons need the AppIndicator extension (Ubuntu has it on by default)."
echo "  Uninstall: sh \"$DEST/uninstall.sh\""
