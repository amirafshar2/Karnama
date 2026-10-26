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

let md = `# تست جمعی Q2 — ۸۳ نفر همزمان\n\n> ساخته‌شده با \`node tests/qa/crowd-report.mjs\` از \`tests/qa/reports/crowd-*/report.json\`. روش: \`docs/qa/QA_PLAN.md\`.\n\n`;
md += `## همه‌ی اجراها\n\n| اجرا | مدت (دقیقه) | درخواست API | p50 / p95 (ms) | خطای 5xx | صفحه‌ی سالم | مشکل | ثابت‌ها |\n|---|---|---|---|---|---|---|---|\n`;
for (const r of runs) {
  const s = r.summary;
  md += `| ${r.dir.replace("crowd-", "").slice(0, 16)} | ${s.minutes} | ${s.calls} | ${s.p50} / ${s.p95} | ${s.errors5xx} | ${s.pagesOk}/${s.pages} | ${s.problems} | ${s.invariantsOk ? "✅" : "❌"} |\n`;
}
const s = last.summary;
md += `\n## آخرین اجرا (${last.dir.replace("crowd-", "").slice(0, 16)})\n\n`;
md += `- **${s.people} نفر** (۸۱ نفر جمعیت + ۲ بازدیدکننده) یک روز کاری کامل (۰۸:۴۰ تا ۱۸:۴۰ به ساعت QA) را در ${s.minutes} دقیقه گذراندند.\n`;
md += `- **${s.calls} درخواست API**؛ p50 = ${s.p50}ms، p95 = ${s.p95}ms؛ خطای سرور (5xx): **${s.errors5xx}**.\n`;
md += `- **${s.pages} بار باز شدن صفحه** در ${s.browsers} مرورگر همزمان (دسکتاپ، گوشی ۳۹۰px، تبلت کیوسک ۷۶۸px، اپ اندروید)؛ سالم: ${s.pagesOk}؛ p95 بارگذاری: ${s.pageP95}ms.\n`;
md += `- پیام تلگرام فرستاده‌شده (درگاه آزمایشی): ${s.telegram?.sent ?? "—"}؛ امضای نامعتبر: ${s.telegram?.badSignature ?? "—"}.\n`;
md += `- ثابت‌های دیتابیس: ${last.invariants.map((i) => `${i.name} ${i.ok ? "✅" : "❌"}`).join("، ")}.\n\n`;

md += `### کندترین درخواست‌ها (p95)\n\n| درخواست | تعداد | p50 | p95 | بیشینه | وضعیت‌ها |\n|---|---|---|---|---|---|\n`;
for (const e of [...last.endpoints].sort((a, b) => b.p95 - a.p95).slice(0, 12))
  md += `| \`${e.endpoint}\` | ${e.n} | ${e.p50} | ${e.p95} | ${e.max} | ${esc(JSON.stringify(e.status))} |\n`;
md += `\n### پرتکرارترین درخواست‌ها\n\n| درخواست | تعداد | p50 | p95 | وضعیت‌ها |\n|---|---|---|---|---|\n`;
for (const e of last.endpoints.slice(0, 15)) md += `| \`${e.endpoint}\` | ${e.n} | ${e.p50} | ${e.p95} | ${esc(JSON.stringify(e.status))} |\n`;

md += `\n### بارگذاری صفحه‌ها به تفکیک دستگاه\n\n| دستگاه | نوع | تعداد | سالم | p50 (ms) | p95 (ms) |\n|---|---|---|---|---|---|\n`;
const groups = {};
for (const p of last.pageStats) (groups[`${p.device}|${p.kind}`] ??= []).push(p);
for (const [k, ps] of Object.entries(groups)) {
  const ms = ps.filter((p) => p.ms).map((p) => p.ms);
  const [device, kind] = k.split("|");
  md += `| ${device} | ${kind === "app" ? "اپ اندروید" : "وب"} | ${ps.length} | ${ps.filter((p) => p.ok).length} | ${pct(ms, 0.5)} | ${pct(ms, 0.95)} |\n`;
}

md += `\n### مشکل‌های آخرین اجرا\n\n`;
if (!last.problems.length) md += "هیچ.\n";
else {
  const g = {};
  for (const p of last.problems) for (const e of p.detail.split(" | ")) { const k = `${p.what} — ${e.slice(0, 140)}`; g[k] = (g[k] ?? 0) + 1; }
  md += `| تعداد | مشکل |\n|---|---|\n` + Object.entries(g).sort((a, b) => b[1] - a[1]).map(([k, n]) => `| ${n} | ${esc(k)} |`).join("\n") + "\n";
}
const notes = [...new Set(last.notes ?? [])];
if (notes.length) md += `\n### یادداشت‌ها\n\n${notes.map((n) => `- ${n}`).join("\n")}\n`;
md += "\n" + readFileSync(join(here, "crowd-findings.md"), "utf8");
writeFileSync(join(here, "../../docs/qa/CROWD_RUN.md"), md);
console.log(`CROWD_RUN.md: ${runs.length} runs`);
