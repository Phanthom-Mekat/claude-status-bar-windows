#!/usr/bin/env node
// Reads the hook JSON on stdin, maps the event to a status, and writes BOTH:
//   ~/.claude/statusbar/state.json            (legacy single-state, last writer wins)
//   ~/.claude/statusbar/sessions.d/<sid>       (this session's own state — the app aggregates these)
// Windows-hardened rename (MoveFileEx is not atomic, throws EPERM/EACCES on a lock -> retry, then drop).
// On a new prompt, relaunches the app if it was quit. Usage: node update.js <prompt|pre|post|notify|permreq|stop>
const fs = require("fs"), os = require("os"), path = require("path"), cp = require("child_process");
const dir = path.join(os.homedir(), ".claude", "statusbar");
const sessDir = path.join(dir, "sessions.d");
const statePath = path.join(dir, "state.json");
const appPathFile = path.join(dir, "apppath.txt");
const event = process.argv[2] || "";
const EXE = "ClaudeStatusBar.exe";

const TOOL_LABELS = {
  Bash: "Running command", Edit: "Editing", Write: "Writing", MultiEdit: "Editing",
  NotebookEdit: "Editing", Read: "Reading", Grep: "Searching", Glob: "Searching",
  WebFetch: "Browsing web", WebSearch: "Searching web", Task: "Delegating", TodoWrite: "Planning",
};

function sleep(ms) { try { Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms); } catch {} }

function writeAtomic(file, data) {
  const tmp = file + "." + process.pid + ".tmp";
  fs.writeFileSync(tmp, data);
  for (let i = 0; i < 3; i++) {
    try { fs.renameSync(tmp, file); return; }
    catch (e) { if (i === 2) { try { fs.rmSync(tmp, { force: true }); } catch {} return; } sleep(15 * (i + 1)); }
  }
}

// Relaunch the app if it was quit (only called on a new prompt, so ~once per turn — cheap).
function launchIfNeeded() {
  try {
    if (!fs.existsSync(appPathFile)) return;
    try {
      const o = cp.execSync(`tasklist /FI "IMAGENAME eq ${EXE}" /NH`, { stdio: ["ignore", "pipe", "ignore"] }).toString();
      if (o.toLowerCase().includes(EXE.toLowerCase())) return; // already running
    } catch {}
    const exe = fs.readFileSync(appPathFile, "utf8").trim();
    if (exe && fs.existsSync(exe)) cp.spawn(exe, [], { detached: true, stdio: "ignore" }).unref();
  } catch {}
}

let raw = "";
process.stdin.on("data", (d) => (raw += d));
process.stdin.on("end", () => {
  let p = {}; try { p = JSON.parse(raw || "{}"); } catch {}

  if (process.env.CLAUDE_STATUSBAR_DEBUG === "1") {
    try { fs.mkdirSync(dir, { recursive: true });
      fs.appendFileSync(path.join(dir, "hooks.log"), `${new Date().toISOString()} [${event}] tool=${p.tool_name || "-"}\n`); } catch {}
  }

  const sid = String(p.session_id || "").replace(/[^A-Za-z0-9_.-]/g, "").slice(0, 64);
  let prev = {}; try { prev = JSON.parse(fs.readFileSync(statePath, "utf8")); } catch {}
  const project = p.cwd ? path.basename(p.cwd) : prev.project || "";
  const ts = Math.floor(Date.now() / 1000);
  let state = "idle", label = "", startedAt = prev.startedAt || 0;

  switch (event) {
    case "prompt": state = "thinking"; label = "Thinking…"; startedAt = ts; break;
    case "pre": { const t = p.tool_name || ""; state = "tool"; label = TOOL_LABELS[t] || "Using tool"; if (!startedAt) startedAt = ts; break; }
    case "post": state = "thinking"; label = "Thinking…"; if (!startedAt) startedAt = ts; break;
    case "notify": {
      const m = (p.message || "").toLowerCase();
      if (m.includes("limit")) { state = "idle"; label = ""; startedAt = 0; break; } // limit ends turn without Stop
      const isPerm = p.notification_type === "permission_prompt" || m.includes("permission") || m.includes("approve") || m.includes("allow");
      if (!isPerm) return;
      state = "permission"; label = "Awaiting permission"; startedAt = 0; break;
    }
    case "permreq": state = "permission"; label = "Awaiting permission"; startedAt = 0; break;
    case "stop": state = "done"; label = ""; startedAt = 0; break;
    default: return;
  }

  const out = { state, label, tool: p.tool_name || "", project, cwd: p.cwd || prev.cwd || "", sessionId: p.session_id || "", transcript: p.transcript_path || prev.transcript || "", startedAt, ts };
  const json = JSON.stringify(out);
  try {
    fs.mkdirSync(sessDir, { recursive: true });
    writeAtomic(statePath, json);                              // legacy single state
    if (sid) writeAtomic(path.join(sessDir, sid), json);       // per-session state (app aggregates)
  } catch {}

  if (event === "prompt") launchIfNeeded();
});
