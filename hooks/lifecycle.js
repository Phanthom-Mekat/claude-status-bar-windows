#!/usr/bin/env node
// SessionStart/End: maintain per-session state files in sessions.d/<sid> (the app aggregates these),
// clear stale state, and launch the exe (path from apppath.txt) on start.
// Windows port of the macOS lifecycle.js (no `open`/LaunchAgent). Usage: node lifecycle.js <start|end>
const fs = require("fs"), os = require("os"), path = require("path"), cp = require("child_process");

const dir = path.join(os.homedir(), ".claude", "statusbar");
const sessDir = path.join(dir, "sessions.d");
const statePath = path.join(dir, "state.json");
const appPathFile = path.join(dir, "apppath.txt");
const appPidFile = path.join(dir, "apppid.txt");
const quitMarker = path.join(dir, "quit-intent");
const event = process.argv[2];

fs.mkdirSync(sessDir, { recursive: true });
const safeId = (s) => String(s || "").replace(/[^A-Za-z0-9_.-]/g, "").slice(0, 64) || "unknown";
const idleState = (id) => JSON.stringify({ state: "idle", label: "", tool: "", project: "", sessionId: id, transcript: "", startedAt: 0, ts: Math.floor(Date.now() / 1000) });

// Host-agnostic running check by the pid the app recorded (it may run as ClaudeStatusBar.exe OR via
// "dotnet ...dll" under Smart App Control). process.kill(pid, 0) is an existence probe on Windows.
function running() {
  try { const pid = parseInt(fs.readFileSync(appPidFile, "utf8"), 10); if (pid > 0) { process.kill(pid, 0); return true; } } catch {}
  return false;
}

function write(file, data) {
  const tmp = file + "." + process.pid + ".tmp";
  try { fs.writeFileSync(tmp, data); for (let i = 0; i < 3; i++) { try { fs.renameSync(tmp, file); return; } catch { if (i === 2) try { fs.rmSync(tmp, { force: true }); } catch {} } } } catch {}
}

// Clear the legacy global state.json if it belongs to this session and is mid-turn.
function clearStaleGlobal(id) {
  try {
    const prev = JSON.parse(fs.readFileSync(statePath, "utf8"));
    if (safeId(prev.sessionId) !== id) return;
    if (!["thinking", "tool", "permission"].includes(prev.state)) return;
    write(statePath, idleState(id));
  } catch {}
}

// The app binary. Prefer the .dll via the Microsoft-signed dotnet host (runs even under Smart App
// Control, which blocks our unsigned exe); fall back to the exe (signed/self-contained builds).
function appCommand(extra) {
  const exe = fs.readFileSync(appPathFile, "utf8").trim();
  const dll = /\.exe$/i.test(exe) ? exe.replace(/\.exe$/i, ".dll") : ""; // Linux binary has no .exe/.dll pair
  return dll && fs.existsSync(dll) ? ["dotnet", [dll, ...extra]] : [exe, extra];
}

function launch() {
  try {
    if (!fs.existsSync(appPathFile) || running()) return;
    const [cmd, args] = appCommand([]);
    cp.spawn(cmd, args, { detached: true, stdio: "ignore", windowsHide: true }).unref();
  } catch {}
}

// This session's claude.exe pid (see update.js claudePid): the app reaps the session when it dies.
function claudePid() {
  try {
    if (!fs.existsSync(appPathFile)) return 0;
    const [cmd, args] = appCommand(["--claude-pid", String(process.pid)]);
    const n = parseInt(cp.execFileSync(cmd, args, { timeout: 5000, windowsHide: true, stdio: ["ignore", "pipe", "ignore"] }).toString(), 10);
    return n > 0 ? n : -1;
  } catch { return -1; }
}

let input = "", done = false;
process.stdin.on("data", (d) => (input += d));
process.stdin.on("end", run);
process.stdin.on("error", run);
setTimeout(run, 1000);

function run() {
  if (done) return; done = true;
  let j = {}; try { j = JSON.parse(input); } catch {}
  const id = safeId(j.session_id);
  const cwd = j.cwd || "";

  if (event === "start") {
    // A new session voids a prior explicit Quit (see update.js's relaunch suppress).
    try { fs.rmSync(quitMarker, { force: true }); } catch {}
    // If the app isn't running, leftover session files are stale (prior crash) -> clear them.
    if (!running()) { try { for (const f of fs.readdirSync(sessDir)) fs.rmSync(path.join(sessDir, f), { force: true }); } catch {} }
    launch(); // first, so the icon appears while we resolve the pid
    // Fresh session starts idle (clears any frozen same-id state from a resume). started:false keeps a
    // merely-opened desktop conversation out of the menu until it has real activity (update.js flips it).
    write(path.join(sessDir, id), JSON.stringify({
      state: "idle", label: "", tool: "", project: cwd ? path.basename(cwd) : "", cwd, sessionId: id, transcript: j.transcript_path || "",
      entrypoint: process.env.CLAUDE_CODE_ENTRYPOINT || "", pid: claudePid(), started: false, startedAt: 0, ts: Math.floor(Date.now() / 1000),
    }));
    clearStaleGlobal(id);
  } else if (event === "end") {
    try { fs.rmSync(path.join(sessDir, id), { force: true }); } catch {}
    clearStaleGlobal(id);
  }
  process.exit(0);
}
