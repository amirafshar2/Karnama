// Gzip size of the JS each page loads on first visit. Budgets (docs/SKILLS.md 7.2): first page (login) < 200 KB, panels < 180 KB.
// Usage: node tools/bundle-size.mjs [src/karnama-web/.next]  (after `next build`)
import { readFileSync, readdirSync, existsSync } from "node:fs";
import { join } from "node:path";
import { gzipSync } from "node:zlib";

const dir = process.argv[2] ?? "src/karnama-web/.next";
const BUDGETS = { login: 200 * 1024, setup: 200 * 1024 };
const PANEL_BUDGET = 180 * 1024;
const appDir = join(dir, "server", "app");
let failed = false;

for (const page of ["login", "dashboard", "roles", "profile", "setup", "settings/brand", "settings/texts", "users", "clients", "packages", "services", "settings/platforms", "work", "orders", "attendance", "attendance/live", "attendance/setup", "attendance/kiosk", "attendance/calendar", "attendance/report"]) {
  const htmlFile = join(appDir, `${page}.html`);
  if (!existsSync(htmlFile)) continue;
  const html = readFileSync(htmlFile, "utf8");
  // Modern browsers skip `noModule` legacy polyfills, so they don't count.
  const tags = [...html.matchAll(/<script[^>]*src="\/_next\/([^"]+\.js)"[^>]*>/g)].filter((m) => !/noModule/i.test(m[0]));
  const scripts = [...new Set(tags.map((m) => m[1]))];
  const total = scripts.reduce((sum, s) => sum + gzipSync(readFileSync(join(dir, s))).length, 0);
  const ok = total <= (BUDGETS[page] ?? PANEL_BUDGET);
  failed ||= !ok;
  console.log(`${ok ? "OK  " : "OVER"} /${page.padEnd(10)} ${(total / 1024).toFixed(1)} KB gzip (${scripts.length} files)`);
}
if (!readdirSync(appDir).length || failed) process.exit(1);
