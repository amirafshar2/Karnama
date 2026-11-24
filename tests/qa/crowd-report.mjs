// Builds docs/qa/CROWD_RUN.md from the crowd runs in tests/qa/reports/crowd-*/report.json (newest last).
// Usage: node tests/qa/crowd-report.mjs
import { readdirSync, readFileSync, writeFileSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const dir = join(here, "reports");
const runs = readdirSync(dir).filter((d) => d.startsWith("crowd-") && existsSync(join(dir, d, "report.json")))
  .sort().map((d) => ({ dir: d, ...JSON.parse(readFileSync(join(dir, d, "report.json"), "utf8")) }))
  .filter((r) => r.summary.browsers > 0); // dry runs without browsers are tooling checks, not crowd runs
const last = runs.at(-1);
const esc = (x) => String(x).replace(/\|/g, "\\|").replace(/\n/g, " ");
const pct = (xs, q) => { const s = [...xs].sort((a, b) => a - b); return s.length ? s[Math.min(s.length - 1, Math.floor(q * s.length))] : 0; };

let md = `# Massentest Q2 – 83 Personen gleichzeitig\n\n> Erstellt mit \`node tests/qa/crowd-report.mjs\` von \`tests/qa/reports/crowd-*/report.json\`. Methode: \`docs/qa/QA_PLAN.md\`.\n\n`;
md += `## Alle Läufe\n\n| Lauf | Dauer (Minuten) | API-Anfragen | p50 / p95 (ms) | 5xx-Fehler | Fehlerfreie Seiten | Probleme | Konstanten |\n|---|---|---|---|---|---|---|---|\n`;
for (const r of runs) {
  const s = r.summary;
  md += `| ${r.dir.replace("crowd-", "").slice(0, 16)} | ${s.minutes} | ${s.calls} | ${s.p50} / ${s.p95} | ${s.errors5xx} | ${s.pagesOk}/${s.pages} | ${s.problems} | ${s.invariantsOk ? "✅" : "❌"} |\n`;
}
const s = last.summary;
md += `\n## Letzter Lauf (${last.dir.replace("crowd-", "").slice(0, 16)})\n\n`;
md += `- **${s.people} Personen** (81 Personen Menge + 2 Besucher) einen vollen Arbeitstag (08:40 bis 18:40 QA-Zeit) in ${s.minutes} Minuten verbracht.\n`;
md += `- **${s.calls} API-Anfragen**; p50 = ${s.p50}ms, p95 = ${s.p95}ms; Serverfehler (5xx): **${s.errors5xx}**.\n`;
md += `- **${s.pages} Seitenaufrufe** in ${s.browsers} Gleichzeitige Browser (Desktop, Handy 390px, Kiosk-Tablet 768px, Android-App); fehlerfrei: ${s.pagesOk}; p95 Laden: ${s.pageP95}ms.\n`;
md += `- gesendete Telegram-Nachricht (Test-Gateway): ${s.telegram?.sent ?? "—"}; ungültige Signatur: ${s.telegram?.badSignature ?? "—"}.\n`;
md += `- Datenbank-Konstanten: ${last.invariants.map((i) => `${i.name} ${i.ok ? "✅" : "❌"}`).join(", ")}.\n\n`;

md += `### Langsamste Anfragen (p95)\n\n| Anfrage | Anzahl | p50 | p95 | Maximum | Status |\n|---|---|---|---|---|---|\n`;
for (const e of [...last.endpoints].sort((a, b) => b.p95 - a.p95).slice(0, 12))
  md += `| \`${e.endpoint}\` | ${e.n} | ${e.p50} | ${e.p95} | ${e.max} | ${esc(JSON.stringify(e.status))} |\n`;
md += `\n### Häufigste Anfragen\n\n| Anfrage | Anzahl | p50 | p95 | Status |\n|---|---|---|---|---|\n`;
for (const e of last.endpoints.slice(0, 15)) md += `| \`${e.endpoint}\` | ${e.n} | ${e.p50} | ${e.p95} | ${esc(JSON.stringify(e.status))} |\n`;

md += `\n### Seitenladen nach Gerät\n\n| Gerät | Typ | Anzahl | Fehlerfrei | p50 (ms) | p95 (ms) |\n|---|---|---|---|---|---|\n`;
const groups = {};
for (const p of last.pageStats) (groups[`${p.device}|${p.kind}`] ??= []).push(p);
for (const [k, ps] of Object.entries(groups)) {
  const ms = ps.filter((p) => p.ms).map((p) => p.ms);
  const [device, kind] = k.split("|");
  md += `| ${device} | ${kind === "app" ? "Android-App" : "Web"} | ${ps.length} | ${ps.filter((p) => p.ok).length} | ${pct(ms, 0.5)} | ${pct(ms, 0.95)} |\n`;
}

md += `\n### Probleme im letzten Lauf\n\n`;
if (!last.problems.length) md += "Keine.\n";
else {
  const g = {};
  for (const p of last.problems) for (const e of p.detail.split(" | ")) { const k = `${p.what} — ${e.slice(0, 140)}`; g[k] = (g[k] ?? 0) + 1; }
  md += `| Anzahl | Problem |\n|---|---|\n` + Object.entries(g).sort((a, b) => b[1] - a[1]).map(([k, n]) => `| ${n} | ${esc(k)} |`).join("\n") + "\n";
}
const notes = [...new Set(last.notes ?? [])];
if (notes.length) md += `\n### Notizen\n\n${notes.map((n) => `- ${n}`).join("\n")}\n`;
md += "\n" + readFileSync(join(here, "crowd-findings.md"), "utf8");
writeFileSync(join(here, "../../docs/qa/CROWD_RUN.md"), md);
console.log(`CROWD_RUN.md: ${runs.length} runs`);
