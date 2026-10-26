// Q2 crowd run (docs/PROMPT3.md §1): 83 people use the system at the same time through one simulated work day.
// - 81 crowd members + 2 anonymous visitors act through the API in parallel (role loops with think time).
// - A rotating pool of real browsers (Playwright) opens the website and the Android app build as crowd members,
//   with the device mix of §1 (desktop, phone, 768px kiosk tablet, Android app build on :3091).
// - A conductor moves the QA clock from 08:40 to 18:40 and runs the scheduled jobs on the way.
// Everything is measured: per-endpoint latency, unexpected statuses, 5xx, browser console/page errors, failed requests.
// Usage (after `bash tests/qa/qa-up.sh`): node tests/qa/crowd.mjs   [CROWD_TICK_MS=30000 CROWD_BROWSERS=5]
import { randomUUID, createHmac } from "node:crypto";
import { createServer } from "node:http";
import { mkdirSync, writeFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { qa, OFFICE_IP, HOME_IP } from "./lib.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const API = (process.env.QA_API ?? "http://localhost:5090").replace(/\/$/, "");
const WEB = process.env.QA_WEB ?? "http://localhost:3090";
const APP = process.env.QA_APP ?? "http://localhost:3091";
// The clock moves 10 QA minutes every 15 s (08:40 → 18:40 in 15 minutes). A step must stay shorter than the 15-minute access
// token: with bigger jumps a page that has just refreshed its token can find it expired a second later, which no real
// user ever sees (the first run used 20-minute steps and showed exactly that as false "logged out" pages).
const TICK_MS = Number(process.env.CROWD_TICK_MS ?? 15_000);   // real time per clock step
const STEP_MIN = Number(process.env.CROWD_STEP_MIN ?? 10);     // QA minutes per step
const BROWSERS = Number(process.env.CROWD_BROWSERS ?? 5);
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
const outDir = join(here, "reports", `crowd-${stamp}`);
mkdirSync(join(outDir, "shots"), { recursive: true });

// ---------------------------------------------------------------- measuring
const calls = new Map();       // "GET /api/v1/work/{id}" → { n, ms[], status{} }
const problems = [];           // unexpected statuses and errors, with who and what
const notes = [];              // things worth a look that are not failures
const route = (p) => p.split("?")[0].replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/g, "{id}").replace(/\/\d+(?=\/|$)/g, "/{n}");
function record(method, path, status, ms) {
  const k = `${method} ${route(path)}`;
  let c = calls.get(k); if (!c) calls.set(k, (c = { n: 0, ms: [], status: {} }));
  c.n++; c.ms.push(ms); c.status[status] = (c.status[status] ?? 0) + 1;
}
const problem = (who, what, detail) => { problems.push({ at: new Date().toISOString(), qaTime: clockNow?.toISOString(), who, what, detail: String(detail).slice(0, 400) }); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const rnd = (a, b) => a + Math.random() * (b - a);
const chance = (p) => Math.random() < p;
const any = (xs) => xs[Math.floor(Math.random() * xs.length)];
let clockNow = null;
let dayOver = false;
const tehranHour = () => { const d = new Date(clockNow.getTime() + 3.5 * 3600_000); return d.getUTCHours() + d.getUTCMinutes() / 60; };

class Person {
  constructor(p, opts = {}) { Object.assign(this, p); this.ip = opts.ip ?? OFFICE_IP; this.android = !!opts.android; this.token = null; this.state = {}; }
  async login() {
    const r = await fetch(`${API}/api/v1/qa/login-as`, { method: "POST", headers: { "Content-Type": "application/json", ...(this.android ? { "X-Client": "android" } : {}) }, body: JSON.stringify({ phone: this.phone }) });
    if (r.status !== 200) throw new Error(`login-as ${this.phone} → ${r.status}`);
    this.token = (await r.json()).accessToken;
    this.signedAt = clockNow;
  }
  /** One API call. `ok` lists statuses that are a normal answer here (a 409 on a race is normal); anything else is a problem. */
  async call(method, path, body, ok = [200, 201, 202, 204]) {
    for (let attempt = 0; attempt < 2; attempt++) {
      if (!this.token || clockNow - this.signedAt > 12 * 60_000) await this.login();
      const h = { Accept: "application/json", Authorization: `Bearer ${this.token}` };
      if (this.ip !== OFFICE_IP) h["X-Forwarded-For"] = this.ip;
      if (this.android) h["X-Client"] = "android";
      if (body !== undefined) h["Content-Type"] = "application/json";
      const t0 = performance.now();
      let res, text;
      try { res = await fetch(API + path, { method, headers: h, body: body === undefined ? undefined : JSON.stringify(body) }); text = await res.text(); }
      catch (e) { record(method, path, "net", performance.now() - t0); problem(this.name, `${method} ${route(path)}`, `network: ${e.message}`); return { status: 0 }; }
      const ms = performance.now() - t0;
      record(method, path, res.status, ms);
      let data = text; try { data = text ? JSON.parse(text) : null; } catch { /* not JSON */ }
      if (res.status === 401 && attempt === 0) { this.token = null; continue; } // token aged on the QA clock: sign in again
      if (res.status >= 500 || !ok.includes(res.status)) problem(this.name, `${method} ${route(path)} → ${res.status}`, typeof data === "string" ? data : JSON.stringify(data));
      return { status: res.status, data };
    }
    return { status: 401 };
  }
  get(p, ok) { return this.call("GET", p, undefined, ok); }
  post(p, b = {}, ok) { return this.call("POST", p, b, ok); }
}

// ---------------------------------------------------------------- the people
const crowd = await qa.crowd();
const by = (g) => crowd.filter((p) => p.group === g);
const [ownerW, ownerA, managerP] = by("lead");
const office = by("office"), field = by("field"), marketers = by("marketer"), writers = by("writer"), [kioskP] = by("kiosk"), clients = by("client");
const P = {
  ownerW: new Person(ownerW), ownerA: new Person(ownerA, { android: true }), manager: new Person(managerP),
  office: office.map((p, i) => new Person(p, { android: i >= 14 })),                   // 14 web, 2 Android
  field: field.map((p, i) => new Person(p, { ip: HOME_IP, android: i >= 2 })),         // outside the office
  marketers: marketers.map((p) => new Person(p)), writers: writers.map((p) => new Person(p)), kiosk: new Person(kioskP),
  clients: clients.map((p, i) => new Person(p, { ip: `5.121.${i}.10`, android: i >= 45 })), // 5 Android clients
};
const staff = [P.ownerW, P.ownerA, P.manager, ...P.office, ...P.field, ...P.marketers, ...P.writers];
const everyone = [...staff, P.kiosk, ...P.clients];
console.log(`[crowd] ${everyone.length} people + 2 anonymous visitors, browsers ${BROWSERS}, ${TICK_MS / 1000}s per ${STEP_MIN} QA minutes`);

// Day plan: two office people arrive late, one does not come (absence → reason), one forgets to clock out.
const late = new Set([P.office[3], P.office[9]]);
const absent = P.office[12];
const forgetsOut = P.office[6];

// ---------------------------------------------------------------- mock Telegram gateway (same as S03)
const TG_SECRET = process.env.QA_TG_SECRET ?? "qa-telegram-gateway-secret-0123456789abcdef";
const telegram = { sent: 0, badSignature: 0 };
const tgServer = createServer((req, res) => {
  let body = "";
  req.on("data", (c) => (body += c));
  req.on("end", () => {
    const ok = createHmac("sha256", TG_SECRET).update(`${req.headers["x-karnama-timestamp"]}.${body}`).digest("hex") === req.headers["x-karnama-signature"];
    res.setHeader("Content-Type", "application/json");
    if (!ok) { telegram.badSignature++; res.statusCode = 401; res.end("{}"); return; }
    telegram.sent++;
    res.end(JSON.stringify({ ok: true, result: { message_id: 10_000 + telegram.sent } }));
  });
});
await new Promise((r) => tgServer.once("error", () => { notes.push("درگاه آزمایشی تلگرام روی :5193 بالا نیامد (پورت مشغول)"); r(); }).listen(5193, "127.0.0.1", r));

// ---------------------------------------------------------------- clock + jobs
await qa.setClock(qa.tehran(8, 40));
clockNow = new Date((await qa.clock()).now);
const jobRuns = {};
async function job(name) {
  const t0 = performance.now();
  try { const n = await qa.job(name); (jobRuns[name] ??= []).push({ n, ms: Math.round(performance.now() - t0) }); }
  catch (e) { problem("job", name, e.message); }
}
async function conductor() {
  const steps = (10 * 60) / STEP_MIN;
  for (let i = 0; i < steps; i++) {
    await sleep(TICK_MS);
    clockNow = new Date((await qa.advance(STEP_MIN)).now);
    const h = tehranHour();
    process.stdout.write(`[crowd] ${String(Math.floor(h)).padStart(2, "0")}:${String(Math.round((h % 1) * 60)).padStart(2, "0")} · calls ${[...calls.values()].reduce((a, c) => a + c.n, 0)} · problems ${problems.length}\n`);
    await job("publish-tick"); await job("outbox"); await job("crm-followups");
    if (h >= 10 && h < 10.2) await job("absences");
    if (h >= 12 && h < 12.2) { await job("asset-alerts"); await job("blog-schedule"); await job("package-expiry"); }
  }
  dayOver = true;
  await sleep(4000);
  await qa.advance(6 * 60);           // past midnight: the forgotten clock-out is closed by the system
  await job("forgotten-shifts");
}

// ---------------------------------------------------------------- shared helpers
const kioskCode = async () => (await P.kiosk.get("/api/v1/attendance/kiosk/code")).data?.code;
const OFFICE_GPS = { latitude: 29.61, longitude: 52.53, accuracy: 15, mock: false };
async function clockIn(p) {
  let evidence = null;
  if (p.ip === OFFICE_IP && !p.android) evidence = { qr: await kioskCode() };
  else if (p.android) evidence = { latitude: OFFICE_GPS.latitude + rnd(-0.0003, 0.0003), longitude: OFFICE_GPS.longitude, accuracy: 12, mock: false };
  const r = await p.post("/api/v1/attendance/clock-in", { idempotencyKey: randomUUID(), evidence }, [200, 409]);
  if (r.status === 200 || r.data?.code === "attendance.alreadyIn") p.state.in = true;
}
async function clockOut(p) {
  const r = await p.post("/api/v1/attendance/clock-out", { idempotencyKey: randomUUID(), evidence: null }, [200, 409]);
  if (r.status === 200) p.state.in = false;
}
const think = () => sleep(rnd(1500, 4500));
let svcCache;
async function services(p) { return (svcCache ??= (await p.get("/api/v1/services/options")).data ?? []); }
const card = async (p, id) => (await p.get(`/api/v1/work/${id}`)).data?.card;

// ---------------------------------------------------------------- role loops
async function worker(p) {
  if (p === absent) { // never comes; after detection writes a reason
    while (!dayOver) {
      await sleep(5000);
      if (tehranHour() > 10.5 && !p.state.reasoned) {
        const open = ((await p.get("/api/v1/leave/absences?open=true")).data ?? [])[0];
        if (open) { await p.post(`/api/v1/leave/absences/${open.id}/reason`, { reason: "بیمار بودم، صبح خبر ندادم" }, [200, 204, 409]); p.state.reasoned = true; }
        else if (tehranHour() > 12) { notes.push(`${p.name}: غایب بود ولی تا ظهر غیبت بازی برایش ثبت نشد`); p.state.reasoned = true; }
      }
    }
    return;
  }
  const arrive = late.has(p) ? 9.6 : rnd(8.7, 9.05);
  while (!dayOver) {
    const h = tehranHour();
    if (!p.state.in && !p.state.left && h >= arrive && h < 17.5) { await clockIn(p); await think(); continue; }
    if (p.state.in && h >= 17.6 && p !== forgetsOut) { await clockOut(p); p.state.left = true; continue; }
    if (p.state.in) {
      const board = (await p.get("/api/v1/work/board?mine=true")).data ?? [];
      const doing = board.find((c) => c.status === "inProgress");
      const next = board.find((c) => c.status === "todo" || c.status === "changesRequested");
      if (doing && chance(0.35)) {
        // the next column depends on the service: admin review, client review, or straight to done (others → 403 work.transition)
        for (const status of ["inReview", "clientReview", "done"]) {
          const r = await p.post(`/api/v1/work/${doing.id}/move`, { status, version: doing.version, note: "تحویل شد" }, [200, 403, 409]);
          if (r.status === 200) { p.state.delivered = (p.state.delivered ?? 0) + 1; break; }
          if (r.status !== 403) break;
        }
      } else if (!doing && next) {
        await p.post(`/api/v1/attendance/timer/${next.id}`, {}, [200, 409]);
        const c = await card(p, next.id);
        if (c) await p.post(`/api/v1/work/${next.id}/move`, { status: "inProgress", version: c.version }, [200, 409]);
      }
      if (chance(0.3)) await p.get("/api/v1/notifications");
      if (chance(0.15)) await p.get("/api/v1/attendance/me");
      if (chance(0.1)) await chatWithManager(p, "این کار رو تا عصر تحویل می‌دم");
      if (!p.state.leaveAsked && chance(0.02) && h > 11) { // a few ask for hourly leave tomorrow
        const types = (await p.get("/api/v1/leave/types")).data ?? [];
        const annual = types.find((t) => t.kind === "annual");
        if (annual) { await p.post("/api/v1/leave/requests", { leaveTypeId: annual.id, hourly: true, from: qa.tehran(10, 0, 1), to: qa.tehran(11, 0, 1), reason: "کار اداری" }, [200, 201, 409, 422]); p.state.leaveAsked = true; }
      }
    }
    await think();
  }
}

let managerId = null;
async function chatWithManager(p, text) {
  managerId ??= (await P.manager.get("/api/v1/me")).data?.id;
  if (!p.state.conv) {
    const contacts = (await p.get("/api/v1/chat/contacts")).data ?? [];
    const m = contacts.find((c) => c.id === managerId) ?? contacts.find((c) => !c.isClient);
    if (!m) return;
    p.state.conv = (await p.post("/api/v1/chat/direct", { userId: m.id }, [200, 201, 403])).data?.id;
  }
  if (p.state.conv) await p.post(`/api/v1/chat/${p.state.conv}/messages`, { body: text, clientKey: randomUUID() });
}

async function manager(p) {
  managerId ??= (await p.get("/api/v1/me")).data?.id;
  while (!dayOver) {
    const h = tehranHour();
    if (!p.state.in && !p.state.left && h >= 8.75) await clockIn(p);
    if (p.state.in && h >= 18.2) { await clockOut(p); p.state.left = true; }
    // new client orders: accept and hand to the suggested person
    const orders = (await p.get("/api/v1/orders?status=submitted&limit=20")).data;
    for (const o of (orders?.items ?? []).slice(0, 4)) {
      const acc = await p.post(`/api/v1/orders/${o.id}/accept?version=${o.version}`, {}, [200, 409]);
      if (acc.status !== 200) continue;
      const board = (await p.get(`/api/v1/work/board?clientId=${o.clientId}`)).data ?? [];
      for (const c of board.filter((c) => c.orderId === o.id && !c.assigneeId && c.status !== "waiting")) {
        const sug = (await p.get(`/api/v1/work/${c.id}/suggestions`)).data ?? [];
        if (sug[0]) await p.post(`/api/v1/work/${c.id}/assign`, { userId: sug[0].userId, version: c.version }, [200, 409]);
      }
    }
    // review queue
    const board = (await p.get("/api/v1/work/board")).data ?? [];
    for (const c of board.filter((c) => c.status === "inReview").slice(0, 4)) {
      const back = chance(0.2);
      await p.post(`/api/v1/work/${c.id}/move`, back ? { status: "changesRequested", version: c.version, note: "کمی روشن‌تر لطفاً" } : { status: "clientReview", version: c.version }, [200, 403, 409]);
    }
    // unassigned items left over
    for (const c of board.filter((c) => !c.assigneeId && c.status === "todo").slice(0, 2)) {
      const sug = (await p.get(`/api/v1/work/${c.id}/suggestions`)).data ?? [];
      if (sug[0]) await p.post(`/api/v1/work/${c.id}/assign`, { userId: sug[0].userId, version: c.version }, [200, 409]);
    }
    // leave requests and live board
    const reqs = (await p.get("/api/v1/leave/requests?pending=true")).data ?? [];
    for (const r of reqs.filter((r) => r.status === "pending" && r.userId !== managerId).slice(0, 2))
      await p.post(`/api/v1/leave/requests/${r.id}/decide`, { approve: chance(0.7), note: "باشه" }, [200, 403, 409]);
    await p.get("/api/v1/attendance/live");
    if (chance(0.3)) await p.get("/api/v1/notifications");
    if (chance(0.2)) {
      const convs = (await p.get("/api/v1/chat")).data;
      const unread = (convs ?? []).filter((c) => c.unread > 0);
      if (unread[0]) { await p.get(`/api/v1/chat/${unread[0].id}/messages`); await p.post(`/api/v1/chat/${unread[0].id}/messages`, { body: "ممنون، دیدم", clientKey: randomUUID() }); }
    }
    await sleep(rnd(3000, 6000));
  }
}

async function owner(p) {
  const from = qa.tehran(0, 0).slice(0, 10), to = from;
  while (!dayOver) {
    await p.get("/api/v1/me");
    await p.get("/api/v1/attendance/live");
    await p.get(any([`/api/v1/reports/work?from=${from}&to=${to}`, `/api/v1/reports/scores?from=${from}&to=${to}`, `/api/v1/leave/report?from=${from}&to=${to}`, `/api/v1/crm/report?from=${from}&to=${to}`, "/api/v1/assets", "/api/v1/clients?q="]));
    await p.get("/api/v1/work/board");
    if (chance(0.3)) await p.get("/api/v1/notifications");
    await sleep(rnd(5000, 9000));
  }
}

async function marketer(p) {
  let n = 0;
  while (!dayOver) {
    const h = tehranHour();
    if (!p.state.in && !p.state.left && h >= 8.8) await clockIn(p);
    if (p.state.in && h >= 17.6) { await clockOut(p); p.state.left = true; }
    if (p.state.in) {
      const leads = (await p.get("/api/v1/crm/leads?limit=30")).data;
      const mine = leads?.items ?? [];
      if (mine.length && chance(0.7)) {
        const l = any(mine);
        await p.post(`/api/v1/crm/leads/${l.id}/calls`, { direction: "outgoing", durationSeconds: Math.round(rnd(0, 240)), outcome: any(["noAnswer", "interested", "later", "rejected"]), note: "تماس از تست جمعی" }, [200, 201, 404, 409, 422]);
      } else if (chance(0.5)) {
        await p.post("/api/v1/crm/leads", { name: `سرنخ جمعی ${p.phone.slice(-2)}-${n++}`, phone: `0937${p.phone.slice(-3)}${String(Math.floor(rnd(1000, 9999)))}`, business: "فروشگاه", city: "شیراز", sourceId: null, tags: ["QA-crowd"], budget: null }, [200, 201, 409]);
      }
      if (chance(0.2)) await p.get("/api/v1/notifications");
    }
    await sleep(rnd(2500, 6000));
  }
}

async function blogWriter(p) {
  const para = (t) => ({ type: "paragraph", content: [{ type: "text", text: t }] });
  let wrote = 0;
  while (!dayOver) {
    const h = tehranHour();
    if (!p.state.in && !p.state.left && h >= 9) await clockIn(p);
    if (p.state.in && h >= 17.6) { await clockOut(p); p.state.left = true; }
    if (p.state.in) {
      await p.get("/api/v1/blog/posts", [200, 403]);
      if (wrote < 2 && chance(0.15)) {
        const body = Array.from({ length: 40 }, (_, i) => `واژه${i}`).join(" ");
        const r = await p.post("/api/v1/blog/posts", { title: `نوشته‌ی جمعی ${p.phone.slice(-2)}-${wrote}`, contentJson: JSON.stringify({ type: "doc", content: [para(body), para(body)] }), categoryId: null, tags: ["qa"], coverImage: null, coverAlt: null, seoTitle: null, metaDescription: null, focusKeyword: null, slug: null, version: null }, [200, 201, 403]);
        if (r.status === 403 && !p.state.noted) { notes.push(`${p.name}: نقش «عکاس و متن‌نویس» اجازه‌ی نوشتن بلاگ ندارد (403) — نویسنده‌ی بلاگ باید نقش جدا داشته باشد`); p.state.noted = true; }
        wrote++;
      }
      if (chance(0.2)) await p.get("/api/v1/help");
    }
    await sleep(rnd(4000, 8000));
  }
}

async function kiosk(p) {
  while (!dayOver) { await p.get("/api/v1/attendance/kiosk/code"); await sleep(25_000); }
}

async function client(p, i) {
  const nearEnd = i >= 40;
  let ordered = 0;
  await sleep(rnd(0, 20_000)); // clients come in through the morning
  while (!dayOver) {
    const home = (await p.get("/api/v1/portal/home")).data;
    for (const a of (home?.approvals ?? []).slice(0, 2)) {
      if (chance(0.8)) await p.post(`/api/v1/portal/approvals/${a.workItemId}`, { approve: true, note: "عالیه", stars: 5 }, [200, 204, 409]);
      else await p.post(`/api/v1/portal/approvals/${a.workItemId}`, { approve: false, note: "رنگ پس‌زمینه را عوض کنید" }, [200, 204, 409]);
    }
    if (ordered < 2 && chance(0.18)) {
      const svc = any(await services(p));
      if (svc) {
        // fill the service's own dynamic form: required text, first option of a select, a date three days ahead
        const form = (await p.get(`/api/v1/services/${svc.id}/form`)).data;
        const answers = {};
        for (const f of form?.fields ?? []) {
          if (f.type === "text" || f.type === "textarea") answers[f.key] = "معرفی محصول جدید";
          else if (f.type === "select" && f.options?.length) answers[f.key] = f.options[0].value ?? f.options[0];
          else if (f.type === "datetime" || f.type === "date") answers[f.key] = new Date(clockNow.getTime() + 3 * 86400_000).toISOString();
        }
        const r = await p.post("/api/v1/portal/orders", { serviceTypeId: svc.id, title: `${svc.name} برای ${p.name}`, priority: "normal", autoAccept: false, answers }, [200, 201, 409, 422]);
        if (r.status === 201 || r.status === 200) ordered++;
        if (r.status === 409) notes.push(`${p.name}: سفارش «${svc.name}» → 409 ${r.data?.code}`);
        if (r.status === 422) notes.push(`${p.name}: فرم سفارش «${svc.name}» 422: ${JSON.stringify(r.data).slice(0, 160)}`);
      }
    }
    if (nearEnd && !p.state.renew && chance(0.2)) { await p.post("/api/v1/portal/renew", { note: "لطفاً تمدید کنید" }, [200, 201, 202, 204, 409, 429]); p.state.renew = true; }
    if (chance(0.15)) await p.get("/api/v1/portal/publishing");
    if (chance(0.2)) await p.get("/api/v1/notifications");
    await sleep(rnd(8000, 20_000));
  }
}

async function visitor(n) {
  const pub = (path) => { const t0 = performance.now(); return fetch(API + path).then(async (r) => { record("GET", path, r.status, performance.now() - t0); if (r.status >= 400) problem(`visitor ${n}`, `GET ${route(path)} → ${r.status}`, ""); return r.status === 200 ? r.json() : null; }); };
  while (!dayOver) {
    await pub("/api/v1/public/site/home");
    const cases = await pub("/api/v1/public/site/cases"); if (cases?.[0]) await pub(`/api/v1/public/site/cases/${encodeURIComponent(cases[0].slug)}`);
    const blog = await pub("/api/v1/public/blog"); if (blog?.items?.[0]) await pub(`/api/v1/public/blog/posts/${encodeURIComponent(blog.items[0].slug)}`);
    await pub("/api/v1/public/site/packages");
    if (chance(0.15)) {
      const t0 = performance.now();
      const r = await fetch(`${API}/api/v1/public/site/leads`, { method: "POST", headers: { "Content-Type": "application/json", "X-Forwarded-For": `5.122.0.${n}` }, body: JSON.stringify({ name: `بازدیدکننده ${n}`, phone: `0938${String(Math.floor(rnd(1e6, 9999999)))}`, business: "کافه", message: "برای پکیج اینستاگرام مشاوره می‌خواهم", source: null, website: null }) });
      record("POST", "/api/v1/public/site/leads", r.status, performance.now() - t0);
      if (r.status !== 202) problem(`visitor ${n}`, `POST consult → ${r.status}`, await r.text());
    }
    await sleep(rnd(6000, 12_000));
  }
}

// ---------------------------------------------------------------- browsers
const require = createRequire(join(here, "../e2e/package.json"));
const { chromium } = require("playwright");
const VIEW = { desktop: { width: 1366, height: 820 }, phone: { width: 390, height: 844, isMobile: true, hasTouch: true }, tablet: { width: 768, height: 1024, hasTouch: true } };
const sessions = [
  // [who, device, kind(web/app), pages]
  [P.ownerW, "desktop", "web", ["/dashboard", "/reports", "/attendance/live", "/assets", "/clients"]],
  [P.ownerA, "phone", "app", ["/dashboard/", "/reports/", "/attendance/live/"]],
  [P.manager, "desktop", "web", ["/dashboard", "/work", "/orders", "/attendance/live", "/chat", "/media"]],
  ...P.office.slice(0, 14).map((p, i) => [p, i % 2 ? "phone" : "desktop", "web", ["/dashboard", "/work", "/attendance", "/chat"]]),
  ...P.office.slice(14).map((p) => [p, "phone", "app", ["/dashboard/", "/work/", "/attendance/"]]),
  ...P.field.map((p, i) => [p, "phone", i >= 2 ? "app" : "web", ["/dashboard", "/attendance", "/assets"].map((x) => (i >= 2 ? x + "/" : x))]),
  ...P.marketers.map((p) => [p, "phone", "web", ["/dashboard", "/crm", "/chat"]]),
  ...P.writers.map((p) => [p, "desktop", "web", ["/dashboard", "/content/blog", "/help"]]),
  [P.kiosk, "tablet", "web", ["/attendance/kiosk"]],
  ...P.clients.map((p, i) => [p, i >= 45 ? "phone" : i >= 40 ? "desktop" : "phone", i >= 45 ? "app" : "web", i >= 45 ? ["/dashboard/", "/chat/"] : ["/dashboard", "/chat", "/publishing"]]),
  [null, "phone", "web", ["/", "/cases", "/blog", "/plans"]],
  [null, "desktop", "web", ["/", "/plans", "/blog"]],
];
const pageStats = []; // { who, device, kind, path, ms, ok, errors[] }
let refreshes401 = 0;
const browserRole = new Map();
async function browserSession(browser, [who, device, kind, pages]) {
  const base = kind === "app" ? APP : WEB;
  const ctx = await browser.newContext({ viewport: { width: VIEW[device].width, height: VIEW[device].height }, isMobile: !!VIEW[device].isMobile, hasTouch: !!VIEW[device].hasTouch, locale: "fa-IR",
    userAgent: kind === "app" ? "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Mobile Safari/537.36 KarnamaApp" : undefined });
  await ctx.addInitScript(() => { try { localStorage.setItem("karnama.tour.off", "1"); } catch { /* ignore */ } });
  const page = await ctx.newPage();
  const label = who ? `${who.name} [${device}/${kind}]` : `بازدیدکننده [${device}]`;
  const errors = [];
  page.on("pageerror", (e) => errors.push(`pageerror: ${e.message}`));
  // A 401 in the console is the normal "access token aged → refresh → retry" path (the QA clock jumps 20 minutes at a time);
  // it is counted, and only a page that ends up on /login counts as a failure.
  page.on("console", (m) => {
    if (m.type() !== "error" || /favicon|net::ERR_ABORTED/.test(m.text())) return;
    if (/status of 401/.test(m.text())) { refreshes401++; return; }
    errors.push(`console: ${m.text().slice(0, 200)}`);
  });
  page.on("response", (r) => { if (r.status() >= 500) errors.push(`HTTP ${r.status()} ${route(new URL(r.url()).pathname)}`); });
  page.on("requestfailed", (r) => { const f = r.failure()?.errorText ?? ""; if (!/ERR_ABORTED|NS_BINDING_ABORTED/.test(f)) errors.push(`requestfailed ${route(new URL(r.url()).pathname)} ${f}`); });
  try {
    if (who) {
      await page.goto(`${base}/login${kind === "app" ? "/" : ""}`, { waitUntil: "domcontentloaded" });
      const res = await page.evaluate(async ({ api, phone, app }) => {
        const r = await fetch(`${api}/api/v1/qa/login-as`, { method: "POST", credentials: "include", headers: { "content-type": "application/json", ...(app ? { "X-Client": "android" } : {}) }, body: JSON.stringify({ phone }) });
        const j = await r.json();
        if (app) localStorage.setItem("capacitor-storage_karnama.refresh", j.refreshToken);
        return r.status;
      }, { api: API, phone: who.phone, app: kind === "app" });
      if (res !== 200) throw new Error(`browser login ${res}`);
    }
    for (const path of pages) {
      const before = errors.length;
      const t0 = performance.now();
      await page.goto(base + path, { waitUntil: "domcontentloaded", timeout: 30_000 });
      let ok = true;
      try { await page.locator("main h1, h1").first().waitFor({ state: "visible", timeout: 15_000 }); } catch { ok = false; errors.push(`no heading on ${path} (url ${new URL(page.url()).pathname})`); }
      if (who && /\/login\/?$/.test(new URL(page.url()).pathname)) { ok = false; errors.push(`bounced to login on ${path}`); }
      const ms = Math.round(performance.now() - t0);
      await page.waitForTimeout(rnd(800, 2500));
      const shotKey = `${kind}-${device}-${who?.group ?? "visitor"}-${path.replace(/\W+/g, "_")}`;
      if (!browserRole.has(shotKey)) { browserRole.set(shotKey, true); await page.screenshot({ path: join(outDir, "shots", `${shotKey}.png`) }).catch(() => undefined); }
      const errs = errors.slice(before);
      pageStats.push({ who: label, device, kind, path, ms, ok: ok && errs.length === 0, errors: errs });
      if (!ok || errs.length) problem(label, `page ${path}`, errs.join(" | "));
    }
  } catch (e) { problem(label, "browser session", e.message); pageStats.push({ who: label, device, kind, path: "(session)", ms: 0, ok: false, errors: [e.message] }); }
  finally { await ctx.close().catch(() => undefined); }
}
async function browsers() {
  const browser = await chromium.launch();
  const queue = [...sessions].sort(() => Math.random() - 0.5);
  // the owner on the phone and the kiosk open first, as in a real morning
  queue.unshift(...queue.splice(queue.findIndex((s) => s[0] === P.kiosk), 1));
  const lanes = Array.from({ length: BROWSERS }, async () => {
    while (queue.length && !dayOver) await browserSession(browser, queue.shift());
  });
  await Promise.all(lanes);
  const left = queue.length;
  if (left) notes.push(`${left} از ${sessions.length} نشست مرورگر به پایان روز نرسید (ظرفیت ${BROWSERS} مرورگر همزمان)`);
  await browser.close();
}

// ---------------------------------------------------------------- run
const t0 = Date.now();
await Promise.all(everyone.map((p) => p.login()));
const loops = [
  conductor(), browsers(), kiosk(P.kiosk), manager(P.manager), owner(P.ownerW), owner(P.ownerA),
  ...P.office.map(worker), ...P.field.map(worker), ...P.marketers.map(marketer), ...P.writers.map(blogWriter),
  ...P.clients.map(client), visitor(1), visitor(2),
];
// Round-robin today's leads to the marketers once the morning starts (the manager's job).
setTimeout(async () => {
  const pool = (await P.manager.get("/api/v1/crm/leads?limit=100")).data?.items ?? [];
  const free = pool.filter((l) => !l.ownerId).map((l) => l.id);
  if (free.length) await P.manager.post("/api/v1/crm/round-robin", { leadIds: free }, [200, 201, 204, 409, 422]);
}, TICK_MS * 2);
await Promise.allSettled(loops);
const invariants = await qa.invariants();
await qa.resetClock();

// ---------------------------------------------------------------- report
const pct = (xs, q) => { const s = [...xs].sort((a, b) => a - b); return s.length ? Math.round(s[Math.min(s.length - 1, Math.floor(q * s.length))]) : 0; };
const endpoints = [...calls].map(([k, c]) => ({ endpoint: k, n: c.n, p50: pct(c.ms, 0.5), p95: pct(c.ms, 0.95), max: Math.round(Math.max(...c.ms)), status: c.status })).sort((a, b) => b.n - a.n);
const allMs = [...calls.values()].flatMap((c) => c.ms);
const total = allMs.length;
const s5xx = [...calls.values()].reduce((a, c) => a + Object.entries(c.status).filter(([s]) => s >= "500" && s !== "net").reduce((x, [, n]) => x + n, 0), 0);
const people = Object.fromEntries(everyone.map((p) => [p.name, { in: !!p.state.in, left: !!p.state.left, delivered: p.state.delivered ?? 0 }]));
const summary = {
  at: new Date().toISOString(), minutes: Math.round((Date.now() - t0) / 60000), people: everyone.length + 2, browsers: BROWSERS,
  calls: total, p50: pct(allMs, 0.5), p95: pct(allMs, 0.95), errors5xx: s5xx, problems: problems.length,
  pages: pageStats.length, pagesOk: pageStats.filter((s) => s.ok).length, pageP95: pct(pageStats.filter((s) => s.ms).map((s) => s.ms), 0.95),
  invariantsOk: invariants.every((i) => i.ok), telegram, browser401s: refreshes401,
};
tgServer.close();
writeFileSync(join(outDir, "report.json"), JSON.stringify({ summary, endpoints, problems, notes, pageStats, jobRuns, invariants, people }, null, 2));
console.log("\n" + JSON.stringify(summary, null, 2));
for (const i of invariants) console.log(`${i.ok ? "OK  " : "BAD "} invariant ${i.name}${i.ok ? "" : ` — ${i.violations}: ${i.detail}`}`);
const grouped = {};
for (const p of problems) grouped[p.what] = (grouped[p.what] ?? 0) + 1;
console.log("problems by kind:", JSON.stringify(Object.entries(grouped).sort((a, b) => b[1] - a[1]).slice(0, 25), null, 1));
for (const n of [...new Set(notes)]) console.log("note:", n);
console.log(`report: ${outDir}`);
process.exit(summary.errors5xx === 0 && summary.problems === 0 && summary.invariantsOk ? 0 : 1);
