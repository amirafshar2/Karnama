// Builds docs/qa/COVERAGE.md from the newest run report (tests/qa/reports/run-*.json): every checked step, by prompt section.
// Usage: node tests/qa/coverage.mjs
import { readdirSync, readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const reports = readdirSync(join(here, "reports")).filter((f) => f.startsWith("run-")).sort();
const last = JSON.parse(readFileSync(join(here, "reports", reports.at(-1)), "utf8"));
const sections = {
  "2.1": "Kommen am Morgen", "2.2": "Aufträge", "2.3": "Telegram-Bot", "2.4": "Ausstattung", "2.5": "CRM", "2.6": "Pakete", "2.7": "Urlaub",
  "2.8": "Chat", "2.9": "Nachmittag", "2.10": "Bericht und Punkte", "2.11": "Öffentliche Website", "2.12": "Admin", "2.13": "Kamera", "2.14": "Hilfe",
};
const steps = last.results.flatMap((r) => r.steps.map((s) => ({ ...s, scenario: r.id })));
const bySec = new Map();
for (const s of steps) {
  const sec = (s.id.match(/^(\d+\.\d+)/) ?? [, "?"])[1];
  if (!bySec.has(sec)) bySec.set(sec, []);
  bySec.get(sec).push(s);
}
const esc = (x) => String(x).replace(/\|/g, "\\|").replace(/\n/g, " ");
let md = `# Abdeckung der Szenarien (docs/PROMPT3.md §2)\n\n> Erstellt aus dem letzten Lauf (\`${reports.at(-1)}\`, ${last.at}) mit \`node tests/qa/coverage.mjs\`. Jede Zeile ist eine automatische Prüfung.\n\n`;
md += `**Summe:** ${steps.filter((s) => s.ok).length}/${steps.length} Prüfung grün · ${last.results.filter((r) => r.ok).length}/${last.results.length} Szenario · Datenbank-Konstanten: ${last.invariants.every((i) => i.ok) ? "Alles grün" : "Rot"}\n\n`;
for (const [sec, list] of [...bySec].sort((a, b) => parseFloat(a[0].split(".")[1]) - parseFloat(b[0].split(".")[1]))) {
  md += `## §${sec} ${sections[sec] ?? ""}\n\n| ID | Prüfung | Ergebnis | Szenario |\n|---|---|---|---|\n`;
  for (const s of list) md += `| ${s.id} | ${esc(s.title)} | ${s.ok ? "✅" : "❌ " + esc(s.error ?? "").slice(0, 160)} | ${s.scenario} |\n`;
  md += "\n";
}
md += `## Datenbank-Konstanten\n\n| Konstante | Ergebnis |\n|---|---|\n` + last.invariants.map((i) => `| ${i.name} | ${i.ok ? "✅" : `❌ ${i.violations}: ${esc(i.detail)}`} |`).join("\n") + "\n\n";
md += readFileSync(join(here, "coverage-elsewhere.md"), "utf8");
mkdirSync(join(here, "../../docs/qa"), { recursive: true });
writeFileSync(join(here, "../../docs/qa/COVERAGE.md"), md);
console.log(`COVERAGE.md: ${steps.length} checks`);
