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
const appPidFile = path.join(dir, "apppid.txt");
const event = process.argv[2];

fs.mkdirSync(sessDir, { recursive: true });
const safeId = (s) => String(s || "").replace(/[^A-Za-z0-9_.-]/g, "").slice(0, 64) || "unknown";
const idleState = (id) => JSON.stringify({ state: "idle", label: "", tool: "", project: "", sessionId: id, transcript: "", startedAt: 0, ts: Math.floor(Date.now() / 1000) });

// Host-agnostic running check (the app may run as ClaudeStatusBar.exe OR via "dotnet ...dll" under
// Smart App Control, so we check by the pid the app recorded).
function running() {
  try {
    const pid = fs.readFileSync(appPidFile, "utf8").trim();
    if (!pid) return false;
    const o = cp.execSync(`tasklist /FI "PID eq ${pid}" /NH`, { stdio: ["ignore", "pipe", "ignore"] }).toString();
    return o.trim().length > 0 && !o.toLowerCase().includes("no tasks");
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

// Launch the app. Prefer the .dll via the Microsoft-signed dotnet host (runs even under Smart App
// Control, which blocks our unsigned exe); fall back to the exe (signed/self-contained builds).
function launch() {
  try {
    if (!fs.existsSync(appPathFile)) return;
    const exe = fs.readFileSync(appPathFile, "utf8").trim();
    const dll = exe.replace(/\.exe$/i, ".dll");
    if (fs.existsSync(dll)) cp.spawn("dotnet", [dll], { detached: true, stdio: "ignore", windowsHide: true }).unref();
    else if (exe && fs.existsSync(exe)) cp.spawn(exe, [], { detached: true, stdio: "ignore" }).unref();
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
