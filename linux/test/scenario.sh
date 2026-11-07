#!/bin/sh
# Runs inside the container (see e2e.sh). Fails fast with "FAIL: …"; prints "E2E PASSED" at the end.
export HOME=/root DISPLAY=:99
SB=$HOME/.claude/statusbar
fail() { echo "FAIL: $*"; echo "--- app.log:"; tail -15 $SB/app.log 2>/dev/null; exit 1; }
export CLAUDE_STATUSBAR_DEBUG=1
hook() { echo "$3" | node "$SB/$1" "$2"; }
shot() { sleep "${2:-1.6}"; import -window "$(xdotool search --name 'Claude Status' | head -1)" /tmp/pill.png; convert /tmp/pill.png -scale 300% "/out/$1.png"; }
header() { python3 /t/sni.py dump /dev/null | sed -n 2p; } # first menu line = live status
menu() { python3 /t/sni.py dump /dev/null; }
# Poll instead of fixed sleeps: a cold container (no font cache, software GL) can take 5-15 s to start the app.
waitfor() { i=0; while ! eval "$1"; do i=$((i + 1)); [ $i -gt 150 ] && return 1; sleep 0.2; done; sleep 0.5; }

Xvfb :99 -screen 0 1280x720x24 >/dev/null 2>&1 &
sleep 1
openbox >/dev/null 2>&1 &
python3 /t/watcher.py > /tmp/watcher.log 2>&1 &
python3 /t/portal.py > /tmp/portal.out 2>&1 &
# Stubs that log instead of acting (no logind/systemd or browser in a container), plus a fake lid + thermal zone.
for c in systemd-inhibit systemctl xdg-open; do
  printf '#!/bin/sh
echo "$@" >> /tmp/%s.log
' "$c" > /usr/local/bin/$c
done
cat >> /usr/local/bin/systemd-inhibit <<'EOS'
while [ $# -gt 0 ]; do case "$1" in --*) shift ;; *) break ;; esac; done
exec "$@"
EOS
chmod +x /usr/local/bin/systemd-inhibit /usr/local/bin/systemctl /usr/local/bin/xdg-open
export CLAUDE_STATUSBAR_TEST_ROOT=/tmp/fake
LID=/tmp/fake/proc/acpi/button/lid/LID0/state TEMP=/tmp/fake/sys/class/thermal/thermal_zone0/temp
mkdir -p "$(dirname $LID)" "$(dirname $TEMP)"; echo "state:      open" > $LID; echo 45000 > $TEMP
cp /bin/dash /tmp/claude; cp /bin/dash /tmp/code   # stand-ins named like the real processes
sleep 1
cp -r /app /opt/csb && chmod +x /opt/csb/ClaudeStatusBar

echo "== first launch installs hooks + registers the tray item"
/opt/csb/ClaudeStatusBar > /tmp/app.out 2>&1 &
waitfor '[ -f $SB/apppath.txt ] && [ -f /tmp/sni.txt ]' || fail "app didn't start: $(cat /tmp/app.out)"
[ "$(cat $SB/apppath.txt)" = /opt/csb/ClaudeStatusBar ] || fail "apppath.txt"
grep -q 'statusbar/update.js.* pre' $HOME/.claude/settings.json || fail "hooks not in settings.json"
grep -q REGISTERED /tmp/watcher.log || fail "tray item never registered with the StatusNotifierWatcher"
python3 /t/sni.py dump /out/tray-idle.png >/dev/null
header | grep -q "^Idle" || fail "idle header"
shot sprout-sleeping 0.2

echo "== SessionStart relaunches a stopped app via apppath.txt"
kill "$(cat $SB/apppid.txt)"; sleep 1.5
pgrep -f /opt/csb/ClaudeStatusBar >/dev/null && fail "app ignored SIGTERM"
rm -f /tmp/sni.txt
hook lifecycle.js start '{"session_id":"s1","cwd":"/home/dev/my_project"}'
waitfor '[ -f /tmp/sni.txt ]' || fail "SessionStart did not relaunch the app"

echo "== states"
hook update.js prompt '{"session_id":"s1","cwd":"/home/dev/my_project","prompt":"hi"}'; shot thinking
header | grep -q "…" || fail "thinking header: $(header)"
hook update.js pre '{"session_id":"s1","cwd":"/home/dev/my_project","tool_name":"Edit"}'; shot editing
header | grep -q "^Editing" || fail "tool header: $(header)"
python3 /t/sni.py dump /out/tray-working.png >/dev/null
hook update.js permreq '{"session_id":"s1","cwd":"/home/dev/my_project","tool_name":"Bash"}'; shot permission
header | grep -q "^Awaiting permission" || fail "permission header: $(header)"
python3 /t/sni.py dump /out/tray-permission.png >/dev/null

echo "== two sessions, pin one"
hook lifecycle.js start '{"session_id":"s2","cwd":"/work/api"}'
hook update.js pre '{"session_id":"s2","cwd":"/work/api","tool_name":"Read"}'
sleep 1.5
header | grep -q "2 active" || fail "active count: $(header)"
menu | grep -q "my__project" || fail "session row missing"
python3 /t/sni.py click "Status follows>api" >/dev/null; sleep 1.5
header | grep -q "^Reading" || fail "pin did not switch the status to the pinned session: $(header)"
menu | grep -q "the status follows one" || fail "pinned header"
python3 /t/sni.py click "Status follows>Busiest" >/dev/null; sleep 1.5
header | grep -q "^Editing\|^Awaiting" || fail "unpin did not go back to the busiest: $(header)"
shot two-sessions

echo "== options persist"
python3 /t/sni.py click "Show timer" >/dev/null; sleep 1
grep -q '"ShowTimer": true' $SB/app-settings.json || fail "Show timer"
for st in Unicode Clawd Orbit; do python3 /t/sni.py click "$st" >/dev/null; shot "anim-$st" 1.2; done
grep -q '"AnimStyle": "mark"' $SB/app-settings.json || fail "animation choice"
python3 /t/sni.py click "Pill mascot" >/dev/null; sleep 1
grep -q '"ShowMascot": false' $SB/app-settings.json || fail "Pill mascot off"
shot mascot-off
python3 /t/sni.py click "Pill mascot" >/dev/null; sleep 1
grep -q '"ShowMascot": true' $SB/app-settings.json || fail "Pill mascot back on"

echo "== pill: click opens the menu, drag moves + saves"
W=$(xdotool search --name "Claude Status" | head -1); [ -n "$W" ] || fail "pill window not found"
eval "$(xdotool getwindowgeometry --shell "$W")"
xdotool mousemove $((X + WIDTH / 2)) $((Y + HEIGHT / 2)) click 1; sleep 1.5
import -window root /out/pill-menu.png
xdotool key Escape; sleep 0.5
xdotool mousemove $((X + WIDTH / 2)) $((Y + HEIGHT / 2)) mousedown 1 mousemove_relative --sync -- -40 -10 \
  sleep 0.2 mousemove_relative --sync -- -300 -250 sleep 0.3 mouseup 1; sleep 1
grep -q '"OverlayX": -1' $SB/app-settings.json && fail "drag position not saved"

echo "== done + claude-pid helper"
hook update.js stop '{"session_id":"s1"}'; hook update.js stop '{"session_id":"s2"}'; sleep 1
header | grep -q "^Done · " || fail "done header: $(header)"
shot sprout-celebrating 0.3
sleep 6.5
header | grep -q "^Idle" || fail "back to idle after Done: $(header)"
cp /bin/dash /tmp/claude
out=$(/tmp/claude -c 'echo $$; sh -c "/opt/csb/ClaudeStatusBar --claude-pid \$\$"; :' | tr '\n' ' ')
set -- $out; [ "$1" = "$2" ] || fail "claude-pid: claude=$1 helper=$2"
[ "$(/opt/csb/ClaudeStatusBar --claude-pid $$)" = -1 ] || fail "claude-pid without a claude ancestor"

echo "== keep PC awake: portal inhibit while working, released when done"
: > /tmp/portal.log
hook update.js pre '{"session_id":"s1","cwd":"/home/dev/my_project","tool_name":"Bash"}'
waitfor 'grep -q "INHIBIT flags=4" /tmp/portal.log' || fail "no portal inhibit while working: $(cat /tmp/portal.out)"
hook update.js stop '{"session_id":"s1"}'
waitfor 'grep -q "^CLOSE" /tmp/portal.log' || fail "inhibit not released when Claude finished"

echo "== lid mode: lock while working, released when done, hibernate when hot with the lid shut"
python3 /t/sni.py click "Keep running with lid closed" >/dev/null
holder() { pgrep -f "kill -0" >/dev/null; }
hook update.js pre '{"session_id":"s1","cwd":"/home/dev/my_project","tool_name":"Bash"}'
waitfor 'holder && grep -q "handle-lid-switch:sleep" /tmp/systemd-inhibit.log' || fail "lid lock not taken while working"
hook update.js stop '{"session_id":"s1"}'
waitfor '! holder' || fail "lid lock still held after Claude finished (lid open)"
hook update.js pre '{"session_id":"s1","cwd":"/home/dev/my_project","tool_name":"Bash"}'
waitfor 'holder' || fail "lid lock not re-taken"
echo "state:      closed" > $LID; echo 91000 > $TEMP
waitfor 'grep -q hibernate /tmp/systemctl.log' || fail "no hibernate at 91 °C with the lid shut"
holder && fail "lid lock still held after hibernating"
echo "state:      open" > $LID; echo 45000 > $TEMP
hook update.js stop '{"session_id":"s1"}'
python3 /t/sni.py click "Keep running with lid closed" >/dev/null; sleep 1
grep -q '"LidMode": false' $SB/app-settings.json || fail "lid mode didn't turn back off"

echo "== click a session row: raise its terminal window; VS Code sessions open their tab"
xterm -T "alpha - xterm" -e /tmp/claude -c 'sleep 600; :' &
sleep 2; AP=$(pgrep -n -x claude)
xterm -T "beta - xterm" -e sleep 600 &
sleep 2
now=$(date +%s)
printf '{"state":"tool","label":"Editing","project":"alpha","cwd":"/work/alpha","sessionId":"jA","pid":%s,"entrypoint":"cli","started":true,"startedAt":%s,"ts":%s}' "$AP" "$now" "$now" > $SB/sessions.d/jA
/tmp/code -c '/tmp/claude -c "sleep 600; :"; :' &
sleep 1; VP=$(pgrep -n -x claude); G=0b5c3f5e-1234-4abc-9def-1234567890ab
printf '{"state":"tool","label":"Reading","project":"webapp","cwd":"/work/webapp","sessionId":"%s","pid":%s,"entrypoint":"claude-vscode","started":true,"startedAt":%s,"ts":%s}' "$G" "$VP" "$now" "$now" > $SB/sessions.d/$G
sleep 2
[ "$(xdotool getactivewindow getwindowname)" = "beta - xterm" ] || fail "test setup: beta should be in front"
python3 /t/sni.py click "alpha" >/dev/null; sleep 1
[ "$(xdotool getactivewindow getwindowname)" = "alpha - xterm" ] || fail "clicking the session didn't raise its terminal (active: $(xdotool getactivewindow getwindowname))"
VSC="$HOME/.config/Code/User"; mkdir -p "$VSC"
printf '{\n    // my comment\n    "editor.fontSize": 14\n}\n' > "$VSC/settings.json"
python3 /t/sni.py click "webapp" >/dev/null; sleep 1.5
grep -q "vscode://anthropic.claude-code/open?session=$G" /tmp/xdg-open.log || fail "VS Code session link not opened: $(cat /tmp/xdg-open.log)"
grep -q '"extensions.confirmedUriHandlerExtensionIds": \["anthropic.claude-code"\]' "$VSC/settings.json" || fail "VS Code not set to trust Claude Code links: $(cat "$VSC/settings.json")"
grep -q "my comment" "$VSC/settings.json" && grep -q fontSize "$VSC/settings.json" || fail "VS Code settings lost the user's content"
[ -f "$VSC/settings.json.bak-claudestatusbar" ] || fail "no settings backup"
rm -f $SB/sessions.d/jA $SB/sessions.d/$G

echo "== Quit"
python3 /t/sni.py click "Quit" >/dev/null; sleep 1.5
pgrep -f /opt/csb/ClaudeStatusBar >/dev/null && fail "still running after Quit"
[ -f $SB/quit-intent ] || fail "quit-intent missing"

echo "== one-click update to v0.6.1 (served from a local release)"
mkdir -p /tmp/rel && tar -czf /tmp/rel/ClaudeStatusBar-linux-x64.tar.gz -C /next .
(cd /tmp/rel && python3 -m http.server 8765 --bind 127.0.0.1 >/dev/null 2>&1 &)
node -e 'const fs=require("fs"),f=process.argv[1],s=JSON.parse(fs.readFileSync(f));s.LatestVersion="0.6.1";s.LastUpdateCheck=Math.floor(Date.now()/1000);fs.writeFileSync(f,JSON.stringify(s))' $SB/app-settings.json
rm -f /tmp/sni.txt
CLAUDE_STATUSBAR_UPDATE_BASE=http://127.0.0.1:8765 /opt/csb/ClaudeStatusBar > /tmp/app2.out 2>&1 &
waitfor '[ -f /tmp/sni.txt ]' || fail "app didn't start for the update test"
menu | grep -q "Update to v0.6.1" || fail "update item missing: $(menu | tail -3)"
before=$(cat /tmp/sni.txt)
python3 /t/sni.py click "⬆ Update" >/dev/null
waitfor '[ "$(cat /tmp/sni.txt)" != "$before" ]' || fail "updated app never came back (no tray registration): $(cat $SB/app.log 2>/dev/null | tail -3)"
menu | grep -q "Version 0.6.1" || fail "still the old version after update: $(menu | tail -3)"
grep -q '"InstalledVersion": "0.6.1.0"' $SB/app-settings.json || fail "InstalledVersion not bumped"
[ "$(pgrep -fc /opt/csb/ClaudeStatusBar)" = 1 ] || fail "expected exactly one running instance"
ls -a /opt | grep -q "csb\.old-" && fail "old install folder not cleaned up"
python3 /t/sni.py click "Quit" >/dev/null
echo "E2E PASSED"
