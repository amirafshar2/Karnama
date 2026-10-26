// S14 — help (docs/PROMPT3.md §2 item 14; PROMPT2.md §13).
// The "؟" article per page (/api/v1/help/page?key=…), audience filtering, the help center search, the first-run tour inputs
// (the tour itself is front-end only: components/help/tour.tsx picks steps from /api/v1/me), the page-help texts for every
// navigation page, and the automated "no form field without help" check (tools/check-help.mjs).
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import { qa, check, eq, pick, DEMO } from "../lib.mjs";

export const id = "S14";
export const title = "help: page articles, audiences, tour inputs, field help check";

const repo = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "..");
const web = join(repo, "src", "karnama-web", "src");
const sfx = Date.now().toString(36).slice(-5);
const doc = (text) => JSON.stringify({ type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text }] }] });

export async function run(rec, { crowd }) {
  await qa.resetClock();
  const admin = await qa.login(DEMO.admin);
  const office = await qa.login(pick(crowd, "office", 1, 0)[0]);
  const marketer = await qa.login(pick(crowd, "marketer", 1, 0)[0]);
  const client = await qa.login(pick(crowd, "client", 1, 0)[0]);

  await rec.step("2.14.a", "the '؟' article for a page: exact key, deepest prefix, unknown page → 204", async () => {
    const att = await office.get("/api/v1/help/page?key=attendance");
    eq(att.pageKey, "attendance", "attendance article");
    const cal = await office.get("/api/v1/help/page?key=attendance/calendar");
    eq(cal.pageKey, "attendance/calendar", "more specific key wins");
    const deep = await office.get("/api/v1/help/page?key=attendance/report/2026");
    eq(deep.pageKey, "attendance", "falls back to the longest matching prefix");
    const none = await qa.anon("GET", "/api/v1/help/page?key=no-such-page", undefined, { token: office.token });
    eq(none.status, 204, "no article → 204");
    const missing = await qa.anon("GET", "/api/v1/help/page", undefined, { token: office.token });
    check(missing.status === 400 || missing.status === 422, `key is required (got ${missing.status})`);
    eq((await qa.anon("GET", "/api/v1/help/page?key=work")).status, 401, "signed-in only");
  });

  await rec.step("2.14.b", "audiences: staff don't get client articles, clients don't get staff articles; the client dashboard has its own", async () => {
    const forClient = await client.get("/api/v1/help");
    check(forClient.length > 0 && forClient.every((a) => a.audience !== "staff"), "client list has no staff articles");
    const dash = await client.get("/api/v1/help/page?key=dashboard");
    eq(dash.audience, "clients", "client dashboard article");
    eq((await qa.anon("GET", "/api/v1/help/page?key=attendance", undefined, { token: client.token })).status, 204, "client: no staff page article");
    const forStaff = await office.get("/api/v1/help");
    check(forStaff.every((a) => a.audience !== "clients"), "staff list has no client articles");
    const staffArticle = forStaff[0];
    const r = await qa.anon("GET", `/api/v1/help/${encodeURIComponent(staffArticle.slug)}`, undefined, { token: client.token });
    eq(r.status, 404, "client can't open a staff article by its slug");
  });

  await rec.step("2.14.c", "PROMPT2 §13 seed articles exist (ورود، ربات، مرخصی، پکیج، تجهیز، تأیید محتوا، پایان پکیج)", async () => {
    const all = await admin.get("/api/v1/help");
    for (const k of ["attendance", "media", "attendance/calendar", "packages", "assets", "dashboard"]) check(all.some((a) => a.pageKey === k), `article for ${k}`);
    check(all.some((a) => a.audience === "clients" && a.pageKey === null), "client 'when does my package end' article");
  });

  await rec.step("2.14.d", "help center search and category filter; drafts visible to editors only; XSS stripped", async () => {
    const hits = await office.get(`/api/v1/help?q=${encodeURIComponent("مرخصی")}`);
    check(hits.length > 0, "search finds the leave article");
    const draft = await admin.post("/api/v1/help", { title: `پیش‌نویس QA ${sfx}`, slug: null, category: "QA", audience: "everyone", pageKey: `qa-help-${sfx}`,
      contentJson: JSON.stringify({ type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "<img src=x onerror=alert(1)>", marks: [{ type: "link", attrs: { href: "javascript:alert(1)" } }] }] }] }),
      sortOrder: 99, isPublished: false });
    check(!/onerror=|javascript:/i.test(draft.contentHtml.replace(/&lt;img src=x onerror=alert\(1\)&gt;/g, "")), `html sanitised: ${draft.contentHtml}`);
    eq((await office.get(`/api/v1/help?category=QA`)).filter((a) => a.id === draft.id).length, 0, "draft hidden from staff");
    eq((await qa.anon("GET", `/api/v1/help/page?key=qa-help-${sfx}`, undefined, { token: office.token })).status, 204, "draft not used for the page button");
    await admin.put(`/api/v1/help/${draft.id}`, { title: `پیش‌نویس QA ${sfx}`, slug: null, category: "QA", audience: "everyone", pageKey: `qa-help-${sfx}`, contentJson: doc("منتشر شد"), sortOrder: 99, isPublished: true });
    eq((await office.get(`/api/v1/help/page?key=qa-help-${sfx}`)).id, draft.id, "published → used for the page");
    eq((await client.get(`/api/v1/help/page?key=qa-help-${sfx}`)).id, draft.id, "'everyone' reaches clients too");
    await admin.del(`/api/v1/help/${draft.id}`);
  });

  await rec.step("2.14.e", "first-run tour inputs per role (tour is client-side; it reads kind + permissions from /api/v1/me)", async () => {
    // tour.tsx: client → client tour; work.assign=all → admin tour; crm.leads without work.view → marketer tour; else worker tour.
    const a = await admin.get("/api/v1/me"); const o = await office.get("/api/v1/me"); const m = await marketer.get("/api/v1/me"); const c = await client.get("/api/v1/me");
    eq(a.permissions["work.assign"], "all", "admin → admin tour");
    eq(c.kind, "client", "client → client tour");
    check(m.permissions["crm.leads"] && !m.permissions["work.view"], `marketer → marketer tour (${JSON.stringify(m.permissions)})`);
    check(o.kind === "staff" && o.permissions["work.assign"] !== "all" && !(o.permissions["crm.leads"] && !o.permissions["work.view"]), "office worker → worker tour");
    const src = readFileSync(join(web, "components", "help", "tour.tsx"), "utf8");
    for (const k of ["tour.clientWelcomeBody", "tour.ordersTitle", "tour.crmTitle", "tour.workTitle", "tour.helpTitle"]) check(src.includes(k), `tour step ${k}`);
  });

  await rec.step("2.14.f", "every navigation page has a '؟' page text (help.page.* in fa-n12.ts)", async () => {
    const nav = readFileSync(join(web, "components", "app", "nav.ts"), "utf8");
    const texts = readFileSync(join(web, "lib", "i18n", "fa-n12.ts"), "utf8");
    const pages = [...new Set([...nav.matchAll(/href: "\/([^"]+)"/g)].map((m) => m[1]))];
    check(pages.length >= 20, `nav pages found: ${pages.length}`);
    const missing = pages.filter((p) => { const parts = p.split("/"); for (let i = parts.length; i > 0; i--) if (texts.includes(`"help.page.${parts.slice(0, i).join("/")}"`)) return false; return true; });
    eq(missing.join(","), "", "pages without a page-help text");
  });

  await rec.step("2.14.g", "automated check: no form field without help text (node tools/check-help.mjs)", async () => {
    let out;
    try { out = execFileSync(process.execPath, [join(repo, "tools", "check-help.mjs")], { encoding: "utf8", timeout: 60_000 }); }
    catch (e) { throw new Error(`check-help failed: ${(e.stdout ?? "") + (e.stderr ?? "")}`.slice(0, 700)); }
    check(/all \d+ fields explained/.test(out), `check-help output: ${out.trim()}`);
  });
}
