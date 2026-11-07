// Node-only hook tests (no deps). Run with an isolated USERPROFILE so the real
// ~/.claude is untouched:  USERPROFILE=<tmp> node hooks/test/run-hook-tests.mjs
import { execFileSync } from "node:child_process";
import fs from "node:fs"; import os from "node:os"; import path from "node:path"; import assert from "node:assert";

const dir = path.join(os.homedir(), ".claude", "statusbar");
const statePath = path.join(dir, "state.json");
const sessDir = path.join(dir, "sessions.d");

function run(script, ev, payload) {
  execFileSync("node", [path.join("hooks", script), ev], { input: JSON.stringify(payload) });
  return JSON.parse(fs.readFileSync(statePath, "utf8"));
}

// --- update.js ---
let s = run("update.js", "pre", { tool_name: "Edit", session_id: "test1", cwd: "C:/proj/foo" });
assert.equal(s.state, "tool"); assert.equal(s.label, "Editing"); assert.equal(s.project, "foo");

// labels name what's being worked on
s = run("update.js", "pre", { tool_name: "Edit", session_id: "test1", tool_input: { file_path: "C:\\proj\\foo\\src\\TrayController.cs" } });
assert.equal(s.label, "Editing TrayController.cs");
s = run("update.js", "pre", { tool_name: "Bash", session_id: "test1", tool_input: { command: "dotnet build", description: "Build the app" } });
assert.equal(s.label, "Build the app");
s = run("update.js", "pre", { tool_name: "Bash", session_id: "test1", tool_input: { command: "ls" } });
assert.equal(s.label, "Running command");
s = run("update.js", "pre", { tool_name: "Read", session_id: "test1", tool_input: { file_path: "/x/" + "a".repeat(40) + ".md" } });
assert.ok(s.label.startsWith("Reading a") && s.label.endsWith("…") && s.label.length <= "Reading ".length + 28);

s = run("update.js", "prompt", { session_id: "test1" });
assert.equal(s.state, "thinking"); assert.ok(s.startedAt > 0);

s = run("update.js", "stop", { session_id: "test1" });
assert.equal(s.state, "done");

// non-permission Notification must NOT change state.json (stays "done")
execFileSync("node", [path.join("hooks", "update.js"), "notify"], { input: JSON.stringify({ message: "Claude is waiting for your input", session_id: "test1" }) });
s = JSON.parse(fs.readFileSync(statePath, "utf8")); assert.equal(s.state, "done");

s = run("update.js", "permreq", { session_id: "test1" }); assert.equal(s.state, "permission");
assert.equal(s.started, true, "any update event marks the session started");
assert.equal(s.pid, 0, "no apppath.txt -> pid unresolved (0), retried on a later event");

// a resolved pid is carried over, never re-resolved
fs.writeFileSync(path.join(sessDir, "test2"), JSON.stringify({ state: "idle", sessionId: "test2", pid: 4242, started: false }));
s = run("update.js", "prompt", { session_id: "test2" });
assert.equal(s.pid, 4242); assert.equal(s.started, true);

// a brand-new session must not inherit another session's pid via the global state.json fallback
s = run("update.js", "prompt", { session_id: "test3" });
assert.equal(s.pid, 0, "pid not leaked from session test2");
console.log("update.js hook tests passed");

// --- lifecycle.js ---
fs.writeFileSync(path.join(dir, "quit-intent"), "");
execFileSync("node", [path.join("hooks", "lifecycle.js"), "start"], { input: JSON.stringify({ session_id: "lc1", cwd: "C:/proj/bar" }) });
assert.ok(fs.existsSync(path.join(sessDir, "lc1")), "session file created on start");
assert.ok(!fs.existsSync(path.join(dir, "quit-intent")), "a new session clears an explicit Quit");
const seeded = JSON.parse(fs.readFileSync(path.join(sessDir, "lc1"), "utf8"));
assert.equal(seeded.started, false); assert.equal(seeded.project, "bar"); assert.equal(seeded.cwd, "C:/proj/bar");
execFileSync("node", [path.join("hooks", "lifecycle.js"), "end"], { input: JSON.stringify({ session_id: "lc1" }) });
assert.ok(!fs.existsSync(path.join(sessDir, "lc1")), "session file removed on end");
console.log("lifecycle.js hook tests passed");

console.log("ALL HOOK TESTS PASSED");
