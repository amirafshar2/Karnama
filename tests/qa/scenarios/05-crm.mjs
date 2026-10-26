// S05 — CRM (docs/PROMPT3.md §2.5, PROMPT2.md §4): the manager creates 100 leads and hands them out round-robin;
// marketers call → "call result" → follow-up; QA clock + "crm-followups" → one reminder; 3 wins → client + package;
// marketer A must not reach marketer B's leads (404 on read, update, calls, move, follow-up, convert).
import { randomInt } from "node:crypto";
import { qa, pick, check, eq } from "../lib.mjs";

export const id = "S05";
export const title = "CRM — ۱۰۰ سرنخ چرخشی، تماس و پیگیری، ۳ برد → مشتری + پکیج، IDOR بازاریاب";

const userId = (actor) => JSON.parse(Buffer.from(actor.token.split(".")[1], "base64url").toString()).sub;
const ANY = Array.from({ length: 500 }, (_, i) => i + 100);
const WON = "0193a000-0000-7000-8000-000000000006";

export async function run(rec, { crowd }) {
  const [managerP] = pick(crowd, "lead", 1, 2);
  const marketersP = pick(crowd, "marketer", 5);
  const run0 = randomInt(10000, 99999);
  const prefix = `0935${run0}`; // 0935 + 5 digits + 2 digits = 11-digit mobile
  await qa.setClock(qa.tehran(10, 0));
  let manager = await qa.login(managerP);
  let mk = await Promise.all(marketersP.map((p) => qa.login(p, { ip: "5.120.33.44" })));
  const relogin = async () => { manager = await qa.login(managerP); mk = await Promise.all(marketersP.map((p) => qa.login(p, { ip: "5.120.33.44" }))); };
  const mid = mk.map(userId);
  const [A, B] = [0, 1];

  const allMine = async (actor) => {
    const out = [];
    let cursor = "";
    for (let i = 0; i < 20; i++) {
      const page = await actor.get(`/api/v1/crm/leads?limit=100&q=${prefix}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ""}`);
      out.push(...page.items);
      if (!page.nextCursor) break;
      cursor = page.nextCursor;
    }
    return out;
  };

  let leads = [];
  await rec.step("2.5.a", "manager creates 100 leads (unassigned); a duplicate phone is refused with 409", async () => {
    for (let i = 0; i < 100; i++) {
      const r = await manager.post("/api/v1/crm/leads", {
        name: `سرنخ QA ${run0}-${i}`, phone: `${prefix}${String(i).padStart(2, "0")}`, business: `کسب‌وکار ${i}`, city: "شیراز",
        sourceId: "0193a000-0000-7000-8000-000000000102", tags: ["QA"], budget: 50_000_000 + i,
      });
      eq(r.lead.ownerId, null, "unassigned until handed out");
      leads.push(r.lead);
    }
    const dup = await manager.call("POST", "/api/v1/crm/leads", { name: "تکراری", phone: `${prefix}00` }, { expect: ANY });
    eq(dup.status, 409, "duplicate phone");
    eq(dup.code, "crm.duplicate", "code");
  });

  await rec.step("2.5.b", "a marketer cannot hand out leads (round-robin / assign → 403)", async () => {
    eq(await mk[A].refused("POST", "/api/v1/crm/round-robin", { leadIds: [leads[0].id] }, [403]), 403, "round-robin");
    eq(await mk[A].refused("POST", `/api/v1/crm/leads/${leads[0].id}/assign`, { ownerId: mid[A] }, [403]), 403, "assign");
  });

  let byOwner = new Map();
  await rec.step("2.5.c", "round-robin of the 100 leads → every marketer in the rotation gets 16 or 17 (difference ≤ 1), all 5 QA marketers included", async () => {
    const r = await manager.post("/api/v1/crm/round-robin", { leadIds: leads.map((l) => l.id) });
    eq(r.assigned, 100, "assigned");
    const all = await allMine(manager);
    eq(all.length, 100, "100 leads found");
    byOwner = new Map();
    for (const l of all) { check(l.ownerId, `lead ${l.name} has an owner`); byOwner.set(l.ownerId, (byOwner.get(l.ownerId) ?? 0) + 1); }
    const counts = [...byOwner.values()];
    check(Math.max(...counts) - Math.min(...counts) <= 1, `even split (${JSON.stringify([...byOwner].map(([k, v]) => [all.find((l) => l.ownerId === k).ownerName, v]))})`);
    for (let i = 0; i < 5; i++) check(byOwner.get(mid[i]) >= 16, `${marketersP[i].name} got ${byOwner.get(mid[i])}`);
    leads = all;
  });

  const mineOf = (i) => leads.filter((l) => l.ownerId === mid[i]);
  await rec.step("2.5.d", "each marketer lists exactly their own leads", async () => {
    for (let i = 0; i < 5; i++) {
      const seen = await allMine(mk[i]);
      eq(seen.length, mineOf(i).length, `${marketersP[i].name} count`);
      check(seen.every((l) => l.ownerId === mid[i]), "only own leads");
    }
  });

  const leadB = () => mineOf(B)[0];
  await rec.step("2.5.e", "IDOR: marketer A cannot open, update, call, move, follow up or convert marketer B's lead (404, nothing changes)", async () => {
    const b = leadB();
    const a = mk[A];
    eq(await a.refused("GET", `/api/v1/crm/leads/${b.id}`, undefined, [404]), 404, "read");
    eq(await a.refused("PUT", `/api/v1/crm/leads/${b.id}`, { name: "هک", phone: b.phone }, [404]), 404, "update");
    eq(await a.refused("POST", `/api/v1/crm/leads/${b.id}/calls`, { direction: "outgoing", outcome: "interested", durationSeconds: 10 }, [404]), 404, "call");
    eq(await a.refused("POST", `/api/v1/crm/leads/${b.id}/activities`, { kind: "note", note: "x" }, [404]), 404, "activity");
    eq(await a.refused("POST", `/api/v1/crm/leads/${b.id}/move`, { stageId: WON }, [404]), 404, "move");
    eq(await a.refused("POST", `/api/v1/crm/leads/${b.id}/follow-up`, { at: qa.tehran(12, 0) }, [404]), 404, "follow-up");
    eq(await a.refused("POST", `/api/v1/crm/leads/${b.id}/convert`, undefined, [404]), 404, "convert");
    eq((await a.get(`/api/v1/crm/leads?q=${b.phone}`)).items.length, 0, "search by B's phone finds nothing");
    const dup = await a.call("POST", "/api/v1/crm/leads", { name: "کپی", phone: b.phone }, { expect: ANY });
    eq(dup.status, 409, "duplicate still detected");
    check(!dup.title.includes(b.name), `duplicate message does not leak B's lead name (${dup.title})`);
    const after = await mk[B].get(`/api/v1/crm/leads/${b.id}`);
    eq(after.lead.name, b.name, "B's lead untouched");
    eq(after.activities.filter((x) => x.kind === "call").length, 0, "no call logged on B's lead");
  });

  let called;
  await rec.step("2.5.f", "marketer A calls: no answer (stays «جدید») then interested with a follow-up in 2h → «تماس اول», follow-up set, in «امروز»", async () => {
    called = mineOf(A)[0];
    const a = mk[A];
    const r1 = await a.post(`/api/v1/crm/leads/${called.id}/calls`, { direction: "outgoing", durationSeconds: 0, outcome: "noAnswer" });
    eq(r1.lead.stageId, called.stageId, "no answer keeps the stage");
    const follow = new Date(Date.parse((await qa.clock()).now) + 2 * 3600_000).toISOString();
    const r2 = await a.post(`/api/v1/crm/leads/${called.id}/calls`, { direction: "outgoing", durationSeconds: 140, outcome: "interested", note: "قیمت پکیج طلایی را خواست", nextFollowUpAt: follow });
    eq(r2.lead.stageId, "0193a000-0000-7000-8000-000000000002", "moved to «تماس اول»");
    eq(Date.parse(r2.lead.nextFollowUpAt), Date.parse(follow), "follow-up stored");
    const calls = r2.activities.filter((x) => x.kind === "call");
    eq(calls.length, 2, "two calls in the timeline");
    check(calls.some((c) => c.outcome === "interested" && c.durationSeconds === 140), "call result kept");
    check((await a.get("/api/v1/crm/today")).some((l) => l.id === called.id), "lead in today's call list");
    const bad = await a.call("POST", `/api/v1/crm/leads/${called.id}/activities`, { kind: "message", note: "بدون کانال" }, { expect: ANY });
    eq(bad.status, 422, "message without channel refused");
    await a.post(`/api/v1/crm/leads/${called.id}/activities`, { kind: "message", channel: "whatsApp", note: "کاتالوگ فرستاده شد" });
  });

  await rec.step("2.5.g", "every marketer logs a call result on one of their leads (5 marketers in parallel)", async () => {
    await Promise.all(mk.map((a, i) => a.post(`/api/v1/crm/leads/${mineOf(i)[1].id}/calls`, {
      direction: "outgoing", durationSeconds: 60 + i, outcome: "later", nextFollowUpAt: qa.tehran(16, 0, 1),
    })));
    const report = await manager.get(`/api/v1/crm/report?from=${qa.tehran(12, 0).slice(0, 10)}&to=${qa.tehran(12, 0).slice(0, 10)}`);
    for (let i = 0; i < 5; i++) {
      const row = report.marketers.find((m) => m.id === mid[i]);
      check(row && row.calls >= 1, `${marketersP[i].name} calls in the report (${JSON.stringify(row)})`);
    }
  });

  await rec.step("2.5.h", "QA clock +3h → crm-followups → marketer A gets one follow-up reminder; re-run: none", async () => {
    await qa.advance(180);
    await relogin();
    const count = async () => (await mk[A].get("/api/v1/notifications")).items.filter((n) => n.kind === "crm.followUp" && n.title.includes(called.name)).length;
    const before = await count();
    await qa.job("crm-followups");
    eq(await count() - before, 1, "one reminder");
    await qa.job("crm-followups");
    eq(await count() - before, 1, "no repeat");
    // B never hears about A's lead.
    check(!(await mk[B].get("/api/v1/notifications")).items.some((n) => n.title.includes(called.name)), "B not reminded of A's lead");
  });

  const won = [];
  await rec.step("2.5.i", "3 leads won (2 moved to «برد» first, 1 converted directly) → each becomes a client; managers notified", async () => {
    const picks = [mineOf(A)[2], mineOf(B)[2], mineOf(2)[2]];
    const owners = [A, B, 2];
    for (let k = 0; k < 3; k++) {
      const a = mk[owners[k]];
      if (k < 2) eq((await a.post(`/api/v1/crm/leads/${picks[k].id}/move`, { stageId: WON })).lead.stageId, WON, "moved to won");
      const c = await a.post(`/api/v1/crm/leads/${picks[k].id}/convert`);
      check(c.clientId, "client id");
      eq(c.existing, false, "new client");
      const d = await a.get(`/api/v1/crm/leads/${picks[k].id}`);
      eq(d.lead.clientId, c.clientId, "lead linked to the client");
      eq(d.lead.stageId, WON, "won stage");
      check(d.activities.some((x) => x.kind === "converted"), "converted in the timeline");
      won.push({ lead: picks[k], clientId: c.clientId });
    }
    const again = await mk[A].post(`/api/v1/crm/leads/${won[0].lead.id}/convert`);
    eq(again.clientId, won[0].clientId, "converting twice returns the same client");
    check((await manager.get("/api/v1/notifications")).items.filter((n) => n.kind === "crm.won").length >= 3, "manager got crm.won");
  });

  await rec.step("2.5.j", "manager assigns a package to each new client (the «پیشنهاد پکیج»); a marketer cannot", async () => {
    const pkgs = (await manager.get("/api/v1/packages?limit=50")).items.filter((p) => p.status === "active" && (p.capacity == null || p.activeClients + 3 <= p.capacity));
    // The QA crowd seed over-fills every demo package (see findings C.md), so a dedicated package is created when needed.
    const pkg = pkgs[0] ?? await manager.post("/api/v1/packages", {
      name: { fa: `پکیج CRM QA ${run0}` }, slug: "", sku: `QAC-${run0}`, category: "مدیریت پیج", tags: [], shortDescription: { fa: "برای مشتری‌های تازه‌ی CRM" },
      descriptionMarkdown: "پکیج آزمایشی سناریو S05", price: 150_000_000, period: "monthly", validityDays: 30, capacity: null, platformIds: [],
      defaultManagement: "agencyManaged", access: "usernameOnly", consultSessions: 1, revisionsPerItem: 2, deliveryHours: 48, features: [], faq: [],
      status: "active", showOnSite: false, sortOrder: 99, badge: "none", quotas: [],
    }, { expect: [200, 201] });
    check(pkg?.id, "a package with free capacity");
    for (const w of won) {
      const c = await manager.post(`/api/v1/clients/${w.clientId}/packages`, { packageId: pkg.id, note: "از CRM" });
      const pkgName = typeof pkg.name === "string" ? pkg.name : pkg.nameValues.fa;
      eq(c.activePackage?.name, pkgName, "client's active package");
      check(c.activePackage.daysLeft > 0, "package running");
      eq(c.mobile ?? w.lead.phone, w.lead.phone, "client keeps the lead's mobile");
    }
    eq(await mk[A].refused("POST", `/api/v1/clients/${won[0].clientId}/packages`, { packageId: pkg.id }, [403]), 403, "marketer cannot assign packages");
    const report = await manager.get(`/api/v1/crm/report?from=${qa.tehran(12, 0).slice(0, 10)}&to=${qa.tehran(12, 0).slice(0, 10)}`);
    const a = report.marketers.find((m) => m.id === mid[A]);
    check(a.won >= 1 && a.sales > 0, `marketer A credited with the sale (${JSON.stringify(a)})`);
  });

  await rec.step("2.5.k", "re-assigning a lead from B to A is logged; B loses access (404)", async () => {
    const l = mineOf(B)[3];
    const r = await manager.post(`/api/v1/crm/leads/${l.id}/assign`, { ownerId: mid[A] });
    eq(r.lead.ownerId, mid[A], "new owner");
    check(r.activities.some((x) => x.kind === "assigned"), "assignment in history");
    eq(await mk[B].refused("GET", `/api/v1/crm/leads/${l.id}`, undefined, [404]), 404, "B lost access");
    eq((await mk[A].get(`/api/v1/crm/leads/${l.id}`)).lead.id, l.id, "A can open it");
    const notMarketer = await manager.call("POST", `/api/v1/crm/leads/${l.id}/assign`, { ownerId: userId(await qa.login(pick(crowd, "office", 1, 0)[0])) }, { expect: ANY });
    eq(notMarketer.status, 422, "cannot assign to a non-marketer");
  });

  await rec.step("2.5.l", "round-robin of explicitly selected leads must not move a won (closed) lead to another marketer (the sale credit would move)", async () => {
    const w = won[0];
    const before = (await manager.get(`/api/v1/crm/leads/${w.lead.id}`)).lead.ownerId;
    // Two turns in a row always name two different marketers, so a won lead that is not skipped moves at least once.
    let after = before;
    for (let t = 0; t < 2 && after === before; t++) {
      await manager.post("/api/v1/crm/round-robin", { leadIds: [w.lead.id] });
      after = (await manager.get(`/api/v1/crm/leads/${w.lead.id}`)).lead.ownerId;
    }
    if (after !== before) await manager.post(`/api/v1/crm/leads/${w.lead.id}/assign`, { ownerId: before }); // put it back
    eq(after, before, "owner of a won lead unchanged by round-robin");
  });

  await rec.step("2.5.m", "«hand to…» list (/crm/marketers) = the rotation: all QA marketers, not the manager", async () => {
    const list = (await manager.get("/api/v1/crm/marketers")).map((m) => m.id);
    for (let i = 0; i < 5; i++) check(list.includes(mid[i]), `${marketersP[i].name} listed`);
    check(!list.includes(userId(manager)), "the manager is not offered as a marketer");
  });
}
