#!/bin/sh
# Build release tarballs for Linux: dist/ClaudeStatusBar-linux-{x64,arm64}.tar.gz
# Self-contained (no .NET runtime needed on the target). Works from Linux, macOS or Git Bash on Windows.
set -eu
cd "$(dirname "$0")"
for rid in linux-x64 linux-arm64; do
  out="dist/$rid"
  rm -rf "$out"
  dotnet publish ClaudeStatusBar.Linux.csproj -c Release -r "$rid" --self-contained -o "$out" -p:DebugType=none
  cp install.sh uninstall.sh Resources/claude-status-bar.png "$out/"
  tar -cf "dist/ClaudeStatusBar-$rid.tar" -C "$out" .
  # Built on Windows the files carry no exec bit (NTFS): re-add the executables with 755 (last entry wins).
  [ -x "$out/ClaudeStatusBar" ] || tar --mode=755 -rf "dist/ClaudeStatusBar-$rid.tar" -C "$out" ./ClaudeStatusBar ./install.sh ./uninstall.sh
  gzip -9f "dist/ClaudeStatusBar-$rid.tar"
  echo "built dist/ClaudeStatusBar-$rid.tar.gz"
done
