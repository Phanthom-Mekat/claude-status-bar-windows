#!/bin/sh
# End-to-end check on a real Linux stack inside Docker: Xvfb + openbox + DBus + a StatusNotifierWatcher standing
# in for the GNOME/KDE panel. Drives the app with the real hooks and asserts; screenshots + tray icon dumps go to
# linux/test/out/. Needs Docker. Run from anywhere (Linux, macOS, Git Bash on Windows):  sh linux/test/e2e.sh
set -eu
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1
here=$(pwd -W 2>/dev/null || pwd)
dotnet publish ClaudeStatusBar.Linux.csproj -c Release -r linux-x64 --self-contained -o dist/linux-x64 -v q -nologo
# The "next release" the one-click update installs: same build, version 0.6.1.
dotnet publish ClaudeStatusBar.Linux.csproj -c Release -r linux-x64 --self-contained -o dist/e2e-next -p:Version=0.6.1 -v q -nologo
docker build -q -t csb-linux-test test >/dev/null
mkdir -p test/out
docker run --rm -v "$here/dist/linux-x64:/app:ro" -v "$here/dist/e2e-next:/next:ro" -v "$here/test:/t:ro" -v "$here/test/out:/out" \
  csb-linux-test dbus-run-session -- sh /t/scenario.sh
