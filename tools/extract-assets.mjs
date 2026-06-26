// Extracts base64 PNG frame data from the macOS Swift sources into PNG files,
// so the Windows app reuses the exact same artwork. The Swift files declare
// quoted base64 literals that start with the PNG magic "iVBORw0KGgo".
import fs from "node:fs"; import path from "node:path";

const SRC = path.resolve("..", "claude-status-bar", "Sources");
const OUT = path.resolve("src", "ClaudeStatusBar", "Resources");
fs.mkdirSync(OUT, { recursive: true });

function b64s(file) {
  const full = path.join(SRC, file);
  if (!fs.existsSync(full)) return [];
  const txt = fs.readFileSync(full, "utf8");
  return [...txt.matchAll(/"(iVBORw0KGgo[A-Za-z0-9+/=\\\s]*?)"/g)]
    .map((m) => m[1].replace(/\\\s*\n\s*/g, "").replace(/\s+/g, ""));
}

const spark = b64s("SparkFrames.swift");
spark.forEach((b, i) => fs.writeFileSync(path.join(OUT, `spark-${String(i).padStart(2, "0")}.png`), Buffer.from(b, "base64")));

const logo = b64s("LogoFrame.swift");
if (logo[0]) fs.writeFileSync(path.join(OUT, "logo.png"), Buffer.from(logo[0], "base64"));

const crab = b64s("CrabFrames.swift"); // Phase 2 style, extracted now for convenience
crab.forEach((b, i) => fs.writeFileSync(path.join(OUT, `crab-${String(i).padStart(2, "0")}.png`), Buffer.from(b, "base64")));

console.log(`extracted ${spark.length} spark frames, ${logo.length ? 1 : 0} logo, ${crab.length} crab frames -> ${OUT}`);
