// Single-actor QA run (docs/PROMPT3.md Q1): every scenario once, in order, then the database invariants.
// Usage: QA_API=http://localhost:5090 node tests/qa/run.mjs [S03 S07 ...]
import { readdirSync, mkdirSync, writeFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { qa, Recorder } from "./lib.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const only = process.argv.slice(2);
const files = readdirSync(join(here, "scenarios")).filter((f) => f.endsWith(".mjs")).sort();
const crowd = await qa.crowd();
const results = [];
for (const f of files) {
  const mod = await import(pathToFileURL(join(here, "scenarios", f)).href);
  if (only.length && !only.includes(mod.id)) continue;
  const rec = new Recorder(mod.id);
  const t0 = performance.now();
  try { await mod.run(rec, { crowd }); }
  catch (e) { rec.steps.push({ id: mod.id, title: "scenario crashed", ok: false, error: String(e?.stack ?? e).slice(0, 1200) }); }
  finally { await qa.resetClock(); }
  results.push({ id: mod.id, title: mod.title, ok: rec.ok, ms: Math.round(performance.now() - t0), steps: rec.steps });
  console.log(`${rec.ok ? "PASS" : "FAIL"} ${mod.id} ${mod.title} (${rec.steps.filter((s) => s.ok).length}/${rec.steps.length})`);
  for (const s of rec.steps.filter((s) => !s.ok)) console.log(`     ✘ [${s.id}] ${s.title}: ${s.error}`);
}
const invariants = await qa.invariants();
for (const i of invariants) console.log(`${i.ok ? "OK  " : "BAD "} invariant ${i.name}${i.ok ? "" : ` — ${i.violations}: ${i.detail}`}`);
const out = join(here, "reports"); mkdirSync(out, { recursive: true });
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
writeFileSync(join(out, `run-${stamp}.json`), JSON.stringify({ at: new Date().toISOString(), results, invariants }, null, 2));
const steps = results.flatMap((r) => r.steps);
console.log(`\n${results.filter((r) => r.ok).length}/${results.length} scenarios · ${steps.filter((s) => s.ok).length}/${steps.length} steps · invariants ${invariants.every((i) => i.ok) ? "green" : "RED"}`);
process.exit(results.every((r) => r.ok) && invariants.every((i) => i.ok) ? 0 : 1);
