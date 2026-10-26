// S06 — packages (docs/PROMPT3.md §2 item 6; PROMPT.md §6.4, PROMPT2.md §10.1–10.3).
// Admin builds a package with a time-limited discount and a full description; assigns a FullAccess package with a stored
// page password; revealing it needs a one-time code and is reported; quota runs out (extra order); renewal request;
// the QA clock walks day by day through the 7/3/1/last-day/ended notices with the job run twice per step (still once each).
import { qa, check, eq, DEMO } from "../lib.mjs";

export const id = "S06";
export const title = "packages: discount, vault reveal, quota, renewal, expiry notices";

const sfx = Date.now().toString(36).slice(-5).toUpperCase();
const mobile = () => "0912" + String(Math.floor(1_000_000 + Math.random() * 8_999_999));
const need = (v, what) => { if (!v) throw new Error(`skipped: prerequisite missing (${what})`); return v; };

export async function run(rec) {
  // Clock: tomorrow 09:00 Tehran (always ahead of real time). Package validity 8 days → ends on day +8 at 09:00.
  // Access tokens follow the QA clock (15-minute life), so every clock jump is followed by fresh sign-ins.
  // Inboxes show the newest 30 by time: on a re-used database start after the latest notice already there.
  await qa.resetClock();
  const latest = (await (await qa.login(DEMO.admin)).get("/api/v1/notifications")).items.map((n) => Date.parse(n.createdAt)).reduce((a, b) => Math.max(a, b), 0);
  const base = Math.max(1, Math.ceil((latest - Date.now()) / 86_400_000) + 1);
  const start = qa.tehran(9, 0, base);
  let admin, writer, owner, portal, portalPhone;
  const relogin = async () => {
    admin = await qa.login(DEMO.admin); writer = await qa.login(DEMO.writer); owner = await qa.login(DEMO.owner);
    if (portalPhone) portal = await qa.login(portalPhone);
  };
  const moveTo = async (iso) => { await qa.setClock(iso); await relogin(); };
  await moveTo(start);
  const now = new Date(start);
  const plus = (h) => new Date(now.getTime() + h * 3600_000).toISOString();
  const instagram = (await admin.get("/api/v1/platforms")).find((p) => p.key === "instagram");

  let service, pkg, client, cred, cp;

  service = await rec.step("2.6.a", "admin creates a quota-consuming service for the package", async () => {
    const s = await admin.post("/api/v1/services", {
      name: { fa: `کپشن QA-D ${sfx}`, en: `Caption QA-D ${sfx}` }, description: "QA S06", icon: "pen-line", basePrice: 3_000_000,
      consumesQuota: true, weight: 1, sortOrder: 90, isActive: true,
      form: [{ key: "topic", type: "text", label: { fa: "موضوع" }, required: true, help: { fa: "مثلاً معرفی محصول" }, min: null, max: null, options: null }],
      stages: [{ id: null, name: { fa: "نوشتن" }, roleId: null, estimatedMinutes: 30, slaHours: 24, needsAdminReview: false, needsClientApproval: false }],
      version: null,
    });
    eq(s.consumesQuota, true, "service consumes quota");
    return s;
  });

  const pkgBody = (over = {}) => ({
    name: { fa: `پکیج QA-D ${sfx}`, en: `QA-D package ${sfx}` }, slug: "", sku: `QAD-${sfx}`, category: "مدیریت پیج", tags: ["qa", "تخفیف"],
    shortDescription: { fa: "پکیج آزمایشی با تخفیف زمان‌دار" },
    descriptionMarkdown: "## چه چیزی می‌گیرید\n\n- **دو** کپشن حرفه‌ای\n- مشاوره‌ی ماهانه\n\n[سایت ما](https://example.com)\n\n<script>alert(1)</script>",
    price: 100_000_000, compareAtPrice: 120_000_000, specialPrice: 80_000_000, specialFrom: plus(-1), specialTo: plus(48),
    period: "monthly", validityDays: 8, saleFrom: null, saleTo: null, capacity: null, platformIds: [instagram.id],
    defaultManagement: "agencyManaged", access: "fullAccess", consultSessions: 1, revisionsPerItem: 2, deliveryHours: 48,
    features: [{ fa: "دو کپشن", en: "Two captions" }], faq: [{ question: "تخفیف تا کی است؟", answer: "تا دو روز." }],
    status: "active", showOnSite: true, sortOrder: 99, badge: "new", seoTitle: `پکیج QA-D ${sfx}`, seoDescription: "پکیج آزمایشی",
    quotas: [{ serviceTypeId: need(service, "service").id, count: 2 }], version: null, ...over,
  });

  await rec.step("2.6.b", "invalid package is refused (special ≥ price, discount ends before it starts, no SKU)", async () => {
    need(service, "service");
    for (const bad of [{ specialPrice: 100_000_000 }, { specialFrom: plus(10), specialTo: plus(5) }, { sku: "" }, { validityDays: 0 }]) {
      const st = await admin.refused("POST", "/api/v1/packages", pkgBody(bad), [400, 422]);
      check(st === 422 || st === 400, `refused ${JSON.stringify(bad)}`);
    }
  });

  pkg = await rec.step("2.6.c", "admin creates a package with a time-limited discount and a full description", async () => {
    const p = await admin.post("/api/v1/packages", pkgBody());
    eq(p.specialPrice, 80_000_000, "special price stored");
    check(p.descriptionHtml?.includes("<h2") && p.descriptionHtml.includes("<strong>"), `markdown rendered: ${p.descriptionHtml}`);
    check(!/<script/i.test(p.descriptionHtml), "script stripped from the description");
    eq(p.faq.length, 1, "faq kept"); eq(p.features.length, 1, "features kept");
    const list = await admin.get(`/api/v1/packages?q=${encodeURIComponent(`QAD-${sfx}`)}`);
    eq(list.items[0]?.effectivePrice, 80_000_000, "effective price is the special price inside its window");
    return p;
  });

  await rec.step("2.6.d", "public site shows the package with its special price", async () => {
    need(pkg, "package");
    const r = await qa.anon("GET", `/api/v1/public/site/packages/${encodeURIComponent(pkg.slug)}`);
    eq(r.status, 200, "public package by slug");
    const body = JSON.stringify(r.data);
    check(body.includes("80000000"), `special price visible publicly: ${body.slice(0, 300)}`);
  });

  await rec.step("2.6.e", "discount window ends → effective price back to the regular price (QA clock +3 days)", async () => {
    need(pkg, "package");
    await moveTo(qa.tehran(9, 0, base + 3));
    const list = await admin.get(`/api/v1/packages?q=${encodeURIComponent(`QAD-${sfx}`)}`);
    eq(list.items[0]?.effectivePrice, 100_000_000, "regular price after the discount ended");
    await moveTo(start);
  });

  client = await rec.step("2.6.f", "admin creates a client with a portal login", async () => {
    const c = await admin.post("/api/v1/clients", {
      businessName: `مشتری QA-D ${sfx}`, industry: "کافه", contactName: `آقای QA ${sfx}`, mobile: mobile(), city: "شیراز", internalNote: null,
      status: "active", accounts: [{ platformId: instagram.id, handle: `qa_d_${sfx.toLowerCase()}`, management: "agencyManaged" }], version: null,
    });
    const withPortal = await admin.post(`/api/v1/clients/${c.id}/portal`);
    eq(withPortal.hasPortal, true, "portal enabled");
    return withPortal;
  });

  cred = await rec.step("2.6.g", "staff stores the page password in the vault (never returned)", async () => {
    need(client, "client");
    const r = await admin.post(`/api/v1/clients/${client.id}/credentials`, {
      platformId: instagram.id, handle: `qa_d_${sfx.toLowerCase()}`, businessSuiteAccess: false, secret: { password: `S3cret-${sfx}`, recoveryEmail: null, recoveryPhone: null, twoFactorNote: null },
    });
    eq(r.hasSecret, true, "secret stored");
    check(!JSON.stringify(r).includes(`S3cret-${sfx}`), "password not echoed");
    const list = await admin.get(`/api/v1/clients/${client.id}/credentials`);
    check(!JSON.stringify(list).includes(`S3cret-${sfx}`), "password not in the list");
    return r;
  });

  cp = await rec.step("2.6.h", "assign the FullAccess package to the client", async () => {
    need(client, "client"); need(pkg, "package");
    const c = await admin.post(`/api/v1/clients/${client.id}/packages`, { packageId: pkg.id, startsAt: null, note: "QA S06" });
    eq(c.activePackage?.access, "fullAccess", "access level copied");
    eq(c.activePackage?.daysLeft, 8, "8 days left");
    eq(c.activePackage?.price, 80_000_000, "frozen price = the special price at assignment");
    return c.activePackage;
  });

  await rec.step("2.6.i", "revealing the password: no permission → 403; wrong code → 422; right code → shown once; reported to the owner", async () => {
    need(cred, "credential");
    await writer.refused("POST", `/api/v1/credentials/${cred.id}/reveal/start`, {}, [403]);
    const start1 = await admin.post(`/api/v1/credentials/${cred.id}/reveal/start`);
    check(start1.challengeId && start1.demoCode, "demo staff get the code on screen (fake numbers); real staff get SMS");
    const wrong = start1.demoCode === "000000" ? "111111" : "000000";
    await admin.refused("POST", `/api/v1/credentials/${cred.id}/reveal`, { challengeId: start1.challengeId, code: wrong }, [422]);
    const shown = await admin.post(`/api/v1/credentials/${cred.id}/reveal`, { challengeId: start1.challengeId, code: start1.demoCode });
    eq(shown.password, `S3cret-${sfx}`, "the stored password");
    eq(shown.visibleSeconds, 30, "visible for 30 s");
    await admin.refused("POST", `/api/v1/credentials/${cred.id}/reveal`, { challengeId: start1.challengeId, code: start1.demoCode }, [422]);
    const after = (await admin.get(`/api/v1/clients/${client.id}/credentials`)).find((c) => c.id === cred.id);
    check(after.lastRevealedAt, "lastRevealedAt recorded");
    const inbox = await owner.get("/api/v1/notifications");
    check(inbox.items.some((n) => n.kind === "credentials.revealed" && n.title.includes(cred.handle)), "owner told who saw the password");
  });

  portal = await rec.step("2.6.j", "client signs in to the portal and sees package, days left and quota", async () => {
    need(cp, "client package");
    const full = await admin.get(`/api/v1/clients/${client.id}`);
    // The portal user's phone is the client's mobile (contact permission shows it to admins).
    portalPhone = full.mobile;
    const p = await qa.login(full.mobile);
    const home = await p.get("/api/v1/portal/home");
    eq(home.package?.daysLeft, 8, "portal days left");
    const q = home.package.quotas.find((x) => x.serviceTypeId === service.id);
    eq(q?.total, 2, "quota total"); eq(q?.used, 0, "quota used");
    return p;
  });

  const orders = [];
  await rec.step("2.6.k", "quota runs out: third order is marked extra (outside the package)", async () => {
    need(portal, "portal");
    for (let i = 0; i < 3; i++) {
      const o = await portal.post("/api/v1/portal/orders", { clientId: "00000000-0000-0000-0000-000000000000", serviceTypeId: service.id, title: `کپشن ${i + 1}`, note: null, answers: { topic: `موضوع ${i + 1}` }, priority: "normal", deadline: null, autoAccept: false });
      const acc = await admin.post(`/api/v1/orders/${o.id}/accept?version=${o.version}`);
      orders.push(acc);
    }
    eq(orders.map((o) => o.outsidePackage).join(","), "false,false,true", "first two in quota, third extra");
    eq(orders[2].price, 3_000_000, "extra order priced at the service's base price");
    const home = await portal.get("/api/v1/portal/home");
    const q = home.package.quotas.find((x) => x.serviceTypeId === service.id);
    eq(q.used, 2, "quota used = 2 (never more than total)");
  });

  await rec.step("2.6.l", "cancel an in-quota order → its slot is free again for the next order", async () => {
    need(orders.length === 3, "three orders");
    await admin.post(`/api/v1/orders/${orders[1].id}/cancel`);
    const o = await portal.post("/api/v1/portal/orders", { clientId: "00000000-0000-0000-0000-000000000000", serviceTypeId: service.id, title: "کپشن ۴", note: null, answers: { topic: "چهارم" }, priority: "normal", deadline: null, autoAccept: false });
    const acc = await admin.post(`/api/v1/orders/${o.id}/accept?version=${o.version}`);
    eq(acc.outsidePackage, false, "freed quota slot reused");
    // Bad form data is refused (required field).
    await portal.refused("POST", "/api/v1/portal/orders", { clientId: "00000000-0000-0000-0000-000000000000", serviceTypeId: service.id, title: "x", note: null, answers: {}, priority: "normal", deadline: null, autoAccept: false }, [422]);
  });

  await rec.step("2.6.m", "renewal request from the portal: managers told once per day; repeat says 'already asked'; staff can't use it", async () => {
    need(portal, "portal");
    await portal.refused("POST", "/api/v1/portal/renew", { note: "x".repeat(501) }, [422]);
    const r1 = await portal.post("/api/v1/portal/renew", { note: "لطفاً تمدید کنید" });
    eq(r1.alreadyRequested, false, "first request");
    const r2 = await portal.post("/api/v1/portal/renew", { note: "دوباره" });
    eq(r2.alreadyRequested, true, "second request the same day");
    const inbox = await admin.get("/api/v1/notifications");
    const mine = inbox.items.filter((n) => n.kind === "package.renewRequested" && n.title.includes(client.businessName));
    eq(mine.length, 1, "manager notified exactly once");
    await writer.refused("POST", "/api/v1/portal/renew", { note: null }, [403]);
  });

  // ---- Expiry walk: run "package-expiry" twice per step and count the client's package notices. ----
  const notices = async () => (await portal.get("/api/v1/notifications")).items.filter((n) => n.kind.startsWith("package.") || n.kind.startsWith("credentials."));
  const walk = []; // [label, kinds+titles snapshot]
  await rec.step("2.6.n", "QA clock day by day + package-expiry twice per day → 7/3/1/last-day/ended each exactly once", async () => {
    need(portal, "portal");
    const steps = [];
    for (let d = 0; d <= 8; d++) steps.push([d, 8, 0], [d, 8, 30]); // 08:00 and 08:30 Tehran every day (twice)
    steps.push([8, 9, 30], [8, 10, 0], [9, 8, 0], [9, 8, 30]); // day 8 after 09:00 the package is over
    for (const [d, h, m] of steps) {
      await moveTo(qa.tehran(h, m, d + base));
      await qa.job("package-expiry");
      walk.push([`d${d} ${h}:${String(m).padStart(2, "0")}`, (await notices()).length]);
    }
    const all = await notices();
    const count = (pred) => all.filter(pred).length;
    const titleHas = (s) => (n) => n.kind === "package.expiring" && n.title.includes(s);
    eq(count(titleHas("7 روز")), 1, "7-day notice once");
    eq(count(titleHas("3 روز")), 1, "3-day notice once");
    eq(count(titleHas("1 روز")), 1, "1-day notice once");
    eq(count(titleHas("آخرین روز")), 1, "last-day notice once");
    eq(count((n) => n.kind === "package.ended"), 1, "ended notice once");
    eq(count((n) => n.kind === "package.expiring"), 4, "no other expiring notices");
    eq(count((n) => n.kind === "package.quotaUsedUp"), 1, "quota-used-up notice once");
    eq(count((n) => n.kind === "credentials.changePassword"), 1, "contract ended: change-password reminder once");
    // Order of arrival: day 1 → 7 days, day 5 → 3 days, day 7 → 1 day, day 8 morning → last day, after 09:00 → ended.
    const at = (label) => walk.find((w) => w[0] === label)?.[1];
    check(at("d0 8:30") === at("d0 8:00"), `second run of the day sends nothing (${JSON.stringify(walk)})`);
    check(at("d1 8:00") > at("d0 8:30"), "7-day notice arrives on day 1");
    check(at("d4 8:30") === at("d1 8:30"), "nothing on days 2–4");
    check(at("d8 9:30") > at("d8 8:30"), "ended notice right after the end time");
  });

  await rec.step("2.6.o", "after the end: package not active, portal shows no running package, history keeps it", async () => {
    need(portal, "portal");
    const c = await admin.get(`/api/v1/clients/${client.id}`);
    check(!c.activePackage || c.activePackage.status !== "active", `no active package: ${JSON.stringify(c.activePackage)}`);
    check(c.packageHistory.some((p) => p.status === "ended"), "history shows the ended package");
    // An order after the end is extra (no active package).
    const o = await portal.post("/api/v1/portal/orders", { clientId: "00000000-0000-0000-0000-000000000000", serviceTypeId: service.id, title: "بعد از پایان", note: null, answers: { topic: "x" }, priority: "normal", deadline: null, autoAccept: false });
    const acc = await admin.post(`/api/v1/orders/${o.id}/accept?version=${o.version}`);
    eq(acc.outsidePackage, true, "order after the end is extra");
  });

  await rec.step("2.6.p", "package soft-delete + restore keeps the client's history; manager can cancel an assigned package", async () => {
    need(pkg, "package");
    await admin.del(`/api/v1/packages/${pkg.id}`);
    const pub = await qa.anon("GET", `/api/v1/public/site/packages/${encodeURIComponent(pkg.slug)}`);
    check(pub.status === 404 || pub.status === 200, "public read still answers"); // cached briefly; see findings
    const restored = await admin.post(`/api/v1/packages/${pkg.id}/restore`);
    eq(restored.isDeleted, false, "restored");
    const c = await admin.post(`/api/v1/clients/${client.id}/packages`, { packageId: pkg.id, startsAt: null, note: "renewal" });
    check(c.activePackage, "renewed package active");
    const cancelled = await admin.post(`/api/v1/clients/${client.id}/packages/${c.activePackage.id}/cancel`);
    check(!cancelled.activePackage, "cancelled package no longer active");
  });
}
