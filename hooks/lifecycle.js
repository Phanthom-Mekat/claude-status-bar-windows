#!/usr/bin/env node
// SessionStart/End: maintain per-session state files in sessions.d/<sid> (the app aggregates these),
// clear stale state, and launch the exe (path from apppath.txt) on start.
// Windows port of the macOS lifecycle.js (no `open`/LaunchAgent). Usage: node lifecycle.js <start|end>
const fs = require("fs"), os = require("os"), path = require("path"), cp = require("child_process");

const EXE = "ClaudeStatusBar.exe";
const dir = path.join(os.homedir(), ".claude", "statusbar");
const sessDir = path.join(dir, "sessions.d");
const statePath = path.join(dir, "state.json");
const appPathFile = path.join(dir, "apppath.txt");
const event = process.argv[2];

fs.mkdirSync(sessDir, { recursive: true });
const safeId = (s) => String(s || "").replace(/[^A-Za-z0-9_.-]/g, "").slice(0, 64) || "unknown";
const idleState = (id) => JSON.stringify({ state: "idle", label: "", tool: "", project: "", sessionId: id, transcript: "", startedAt: 0, ts: Math.floor(Date.now() / 1000) });

function running() {
  try {
    const o = cp.execSync(`tasklist /FI "IMAGENAME eq ${EXE}" /NH`, { stdio: ["ignore", "pipe", "ignore"] }).toString();
    return o.toLowerCase().includes(EXE.toLowerCase());
  } catch { return false; }
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

function launch() {
  try {
    if (!fs.existsSync(appPathFile)) return;
    const exe = fs.readFileSync(appPathFile, "utf8").trim();
    if (exe && fs.existsSync(exe)) cp.spawn(exe, [], { detached: true, stdio: "ignore" }).unref();
  } catch {}
}

let input = "", done = false;
process.stdin.on("data", (d) => (input += d));
process.stdin.on("end", run);
process.stdin.on("error", run);
setTimeout(run, 1000);

function run() {
  if (done) return; done = true;
  let id = ""; try { id = JSON.parse(input).session_id; } catch {}
  id = safeId(id);

  if (event === "start") {
    // If the app isn't running, leftover session files are stale (prior crash) -> clear them.
    if (!running()) { try { for (const f of fs.readdirSync(sessDir)) fs.rmSync(path.join(sessDir, f), { force: true }); } catch {} }
    write(path.join(sessDir, id), idleState(id)); // fresh session starts idle (clears any stale same-id state)
    clearStaleGlobal(id);
    launch();
  } else if (event === "end") {
    try { fs.rmSync(path.join(sessDir, id), { force: true }); } catch {}
    clearStaleGlobal(id);
  }
  process.exit(0);
}
