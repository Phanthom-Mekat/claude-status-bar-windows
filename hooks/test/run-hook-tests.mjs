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

s = run("update.js", "prompt", { session_id: "test1" });
assert.equal(s.state, "thinking"); assert.ok(s.startedAt > 0);

s = run("update.js", "stop", { session_id: "test1" });
assert.equal(s.state, "done");

// non-permission Notification must NOT change state.json (stays "done")
execFileSync("node", [path.join("hooks", "update.js"), "notify"], { input: JSON.stringify({ message: "Claude is waiting for your input", session_id: "test1" }) });
s = JSON.parse(fs.readFileSync(statePath, "utf8")); assert.equal(s.state, "done");

s = run("update.js", "permreq", { session_id: "test1" }); assert.equal(s.state, "permission");
console.log("update.js hook tests passed");

// --- lifecycle.js ---
execFileSync("node", [path.join("hooks", "lifecycle.js"), "start"], { input: JSON.stringify({ session_id: "lc1" }) });
assert.ok(fs.existsSync(path.join(sessDir, "lc1")), "session file created on start");
execFileSync("node", [path.join("hooks", "lifecycle.js"), "end"], { input: JSON.stringify({ session_id: "lc1" }) });
assert.ok(!fs.existsSync(path.join(sessDir, "lc1")), "session file removed on end");
console.log("lifecycle.js hook tests passed");

console.log("ALL HOOK TESTS PASSED");
