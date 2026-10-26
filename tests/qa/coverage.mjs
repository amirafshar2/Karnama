// Builds docs/qa/COVERAGE.md from the newest run report (tests/qa/reports/run-*.json): every checked step, by prompt section.
// Usage: node tests/qa/coverage.mjs
import { readdirSync, readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const reports = readdirSync(join(here, "reports")).filter((f) => f.startsWith("run-")).sort();
const last = JSON.parse(readFileSync(join(here, "reports", reports.at(-1)), "utf8"));
const sections = {
  "2.1": "ورود صبح", "2.2": "سفارش‌ها", "2.3": "ربات تلگرام", "2.4": "تجهیزات", "2.5": "CRM", "2.6": "پکیج‌ها", "2.7": "مرخصی",
  "2.8": "گفتگو", "2.9": "عصر", "2.10": "گزارش و امتیاز", "2.11": "سایت عمومی", "2.12": "ادمین", "2.13": "دوربین", "2.14": "راهنما",
};
const steps = last.results.flatMap((r) => r.steps.map((s) => ({ ...s, scenario: r.id })));
const bySec = new Map();
for (const s of steps) {
  const sec = (s.id.match(/^(\d+\.\d+)/) ?? [, "?"])[1];
  if (!bySec.has(sec)) bySec.set(sec, []);
  bySec.get(sec).push(s);
}
const esc = (x) => String(x).replace(/\|/g, "\\|").replace(/\n/g, " ");
let md = `# پوشش سناریوها (docs/PROMPT3.md §2)\n\n> ساخته‌شده از آخرین اجرا (\`${reports.at(-1)}\`، ${last.at}) با \`node tests/qa/coverage.mjs\`. هر ردیف یک بررسی خودکار است.\n\n`;
md += `**جمع:** ${steps.filter((s) => s.ok).length}/${steps.length} بررسی سبز · ${last.results.filter((r) => r.ok).length}/${last.results.length} سناریو · ثابت‌های دیتابیس: ${last.invariants.every((i) => i.ok) ? "همه سبز" : "قرمز"}\n\n`;
for (const [sec, list] of [...bySec].sort((a, b) => parseFloat(a[0].split(".")[1]) - parseFloat(b[0].split(".")[1]))) {
  md += `## §${sec} ${sections[sec] ?? ""}\n\n| شناسه | بررسی | نتیجه | سناریو |\n|---|---|---|---|\n`;
  for (const s of list) md += `| ${s.id} | ${esc(s.title)} | ${s.ok ? "✅" : "❌ " + esc(s.error ?? "").slice(0, 160)} | ${s.scenario} |\n`;
  md += "\n";
}
md += `## ثابت‌های دیتابیس\n\n| ثابت | نتیجه |\n|---|---|\n` + last.invariants.map((i) => `| ${i.name} | ${i.ok ? "✅" : `❌ ${i.violations}: ${esc(i.detail)}`} |`).join("\n") + "\n\n";
md += readFileSync(join(here, "coverage-elsewhere.md"), "utf8");
mkdirSync(join(here, "../../docs/qa"), { recursive: true });
writeFileSync(join(here, "../../docs/qa/COVERAGE.md"), md);
console.log(`COVERAGE.md: ${steps.length} checks`);
