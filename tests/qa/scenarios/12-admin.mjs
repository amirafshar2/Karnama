// S12 — admin (docs/PROMPT3.md §2 item 12; PROMPT.md §5–6.10, PROMPT2.md).
// CRUD with soft-delete + restore for the main entities, a new role applied live to a user, English/LTR,
// brand colours, and that /setup can't be re-run on this (demo) install.
import { qa, check, eq, DEMO } from "../lib.mjs";

export const id = "S12";
export const title = "admin: CRUD + restore, live roles, English/LTR, brand, setup";

const sfx = Date.now().toString(36).slice(-5);
const mobile = (p = "0913") => p + String(Math.floor(1_000_000 + Math.random() * 8_999_999));
const need = (v, what) => { if (!v) throw new Error(`skipped: prerequisite missing (${what})`); return v; };
const doc = (text) => JSON.stringify({ type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text }] }] });

export async function run(rec) {
  await qa.resetClock();
  const admin = await qa.login(DEMO.admin);
  const writer = await qa.login(DEMO.writer);

  // ---------------- CRUD + soft delete + restore ----------------
  await rec.step("2.12.a", "users: create, validation, update, soft-delete (gone from list, kept with includeDeleted), restore", async () => {
    await admin.refused("POST", "/api/v1/users", { displayName: "بد", phone: "123", roleIds: [], isActive: true }, [422]);
    const u = await admin.post("/api/v1/users", { displayName: `کارمند QA-D ${sfx}`, phone: mobile(), roleIds: [], isActive: true });
    await admin.refused("POST", "/api/v1/users", { displayName: "تکراری", phone: u.phone ?? "", roleIds: [], isActive: true }, [409, 422]);
    const up = await admin.put(`/api/v1/users/${u.id}`, { displayName: `کارمند QA-D ${sfx} ویرایش`, phone: u.phone, roleIds: [], isActive: true });
    check(up.displayName.endsWith("ویرایش"), "renamed");
    await admin.del(`/api/v1/users/${u.id}`);
    const list = await admin.get(`/api/v1/users?q=${encodeURIComponent(`QA-D ${sfx}`)}`);
    eq(list.items.length, 0, "deleted user hidden");
    const all = await admin.get(`/api/v1/users?q=${encodeURIComponent(`QA-D ${sfx}`)}&includeDeleted=true`);
    eq(all.items.length, 1, "deleted user still there with includeDeleted");
    const back = await admin.post(`/api/v1/users/${u.id}/restore`);
    check(!back.isDeleted, "restored");
    await admin.refused("DELETE", `/api/v1/users/${(await admin.get("/api/v1/me")).id}`, undefined, [409]);
  });

  await rec.step("2.12.b", "services: create, update with version, stale version → 409, delete, restore", async () => {
    const body = (v) => ({ name: { fa: `خدمت QA-D ${sfx}` }, description: null, icon: "pen-line", basePrice: 1_000_000, consumesQuota: false, weight: 1, sortOrder: 95, isActive: true,
      form: [{ key: "note", type: "textarea", label: { fa: "توضیح" }, required: false, help: { fa: "هر چه لازم است" }, min: null, max: null, options: null }],
      stages: [{ id: null, name: { fa: "انجام" }, roleId: null, estimatedMinutes: 60, slaHours: 24, needsAdminReview: false, needsClientApproval: false }], version: v });
    await admin.refused("POST", "/api/v1/services", { ...body(null), stages: [] }, [422]);
    const s = await admin.post("/api/v1/services", body(null));
    const s2 = await admin.put(`/api/v1/services/${s.id}`, { ...body(s.version), basePrice: 2_000_000 });
    eq(s2.basePrice, 2_000_000, "updated");
    await admin.refused("PUT", `/api/v1/services/${s.id}`, { ...body(s.version), basePrice: 3_000_000 }, [409]);
    await admin.del(`/api/v1/services/${s.id}`);
    eq((await admin.get(`/api/v1/services?q=${encodeURIComponent(`QA-D ${sfx}`)}`)).items.length, 0, "hidden after delete");
    const r = await admin.post(`/api/v1/services/${s.id}/restore`);
    check(!r.isDeleted, "restored");
  });

  await rec.step("2.12.c", "packages: update with stale version → 409; delete + restore; export works", async () => {
    const svc = (await admin.get("/api/v1/services?limit=1")).items[0];
    const body = (v, over = {}) => ({ name: { fa: `پکیج ادمین ${sfx}` }, slug: "", sku: `ADM-${sfx.toUpperCase()}`, category: null, tags: [], shortDescription: null, descriptionMarkdown: "متن",
      price: 5_000_000, compareAtPrice: null, specialPrice: null, specialFrom: null, specialTo: null, period: "oneTime", validityDays: 30, saleFrom: null, saleTo: null,
      capacity: 3, platformIds: [], defaultManagement: "clientManaged", access: "usernameOnly", consultSessions: 0, revisionsPerItem: 1, deliveryHours: 24, features: [], faq: [],
      status: "draft", showOnSite: false, sortOrder: 98, badge: "none", seoTitle: null, seoDescription: null, quotas: [{ serviceTypeId: svc.id, count: 1 }], version: v, ...over });
    const p = await admin.post("/api/v1/packages", body(null));
    const p2 = await admin.put(`/api/v1/packages/${p.id}`, body(p.version, { price: 6_000_000 }));
    eq(p2.price, 6_000_000, "price updated");
    await admin.refused("PUT", `/api/v1/packages/${p.id}`, body(p.version, { price: 7_000_000 }), [409]);
    const hist = await admin.get(`/api/v1/history/Package/${p.id}`);
    check(JSON.stringify(hist).includes("packages.updated"), "price change in the history");
    await admin.del(`/api/v1/packages/${p.id}`);
    eq((await admin.get(`/api/v1/packages?q=${encodeURIComponent(`ADM-${sfx}`)}`)).items.length, 0, "hidden after delete");
    // A deleted package frees its SKU/slug; restoring it while a new one uses them is refused (409), after that it works.
    const twin = await admin.post("/api/v1/packages", body(null));
    await admin.refused("POST", `/api/v1/packages/${p.id}/restore`, {}, [409]);
    await admin.del(`/api/v1/packages/${twin.id}`);
    const r = await admin.post(`/api/v1/packages/${p.id}/restore`);
    check(!r.isDeleted, "restored");
    const x = await fetch(`${process.env.QA_API}/api/v1/packages/export`, { headers: { Authorization: `Bearer ${admin.token}` } });
    eq(x.status, 200, "excel export");
  });

  await rec.step("2.12.d", "clients: create, bad mobile → 422, update, delete, restore", async () => {
    const insta = (await admin.get("/api/v1/platforms")).find((p) => p.key === "instagram");
    const body = (v, over = {}) => ({ businessName: `مشتری ادمین ${sfx}`, industry: null, contactName: null, mobile: mob, city: "تهران", internalNote: null, status: "active",
      accounts: [{ platformId: insta.id, handle: `adm_${sfx}`, management: "agencyManaged" }], version: v, ...over });
    const mob = mobile("0914");
    await admin.refused("POST", "/api/v1/clients", body(null, { mobile: "abc" }), [422]);
    const c = await admin.post("/api/v1/clients", body(null));
    const c2 = await admin.put(`/api/v1/clients/${c.id}`, body(c.version, { city: "شیراز", status: "paused" }));
    eq(c2.city, "شیراز", "updated"); eq(c2.status, "paused", "paused");
    await admin.del(`/api/v1/clients/${c.id}`);
    eq((await admin.get(`/api/v1/clients?q=${encodeURIComponent(`ادمین ${sfx}`)}`)).items.length, 0, "hidden");
    const r = await admin.post(`/api/v1/clients/${c.id}/restore`);
    check(!r.isDeleted, "restored");
  });

  await rec.step("2.12.e", "platforms: create, duplicate key → 409, update, delete, restore", async () => {
    const key = `qa${sfx}`;
    const p = await admin.post("/api/v1/platforms", { key, name: { fa: `شبکه ${sfx}`, en: `Net ${sfx}` }, urlTemplate: "https://example.com/{handle}", sortOrder: 50, isActive: true, version: null });
    await admin.refused("POST", "/api/v1/platforms", { key, name: { fa: "دوباره" }, urlTemplate: null, sortOrder: 51, isActive: true, version: null }, [409]);
    const p2 = await admin.put(`/api/v1/platforms/${p.id}`, { key, name: { fa: `شبکه ${sfx} ۲` }, urlTemplate: null, sortOrder: 50, isActive: false, version: p.version });
    eq(p2.isActive, false, "deactivated");
    await admin.del(`/api/v1/platforms/${p.id}`);
    check(!(await admin.get("/api/v1/platforms")).some((x) => x.id === p.id), "hidden");
    const r = await admin.post(`/api/v1/platforms/${p.id}/restore`);
    check(!r.isDeleted, "restored");
  });

  await rec.step("2.12.f", "leave types: create, bad colour → 422, update/deactivate (no delete/restore endpoint — see findings)", async () => {
    await admin.refused("POST", "/api/v1/leave/types", { name: "بد", kind: "other", paid: false, needsDocument: false, annualDays: null, color: "red", sortOrder: 1, isActive: true }, [422]);
    const t = await admin.post("/api/v1/leave/types", { name: `مرخصی QA ${sfx}`, kind: "other", paid: true, needsDocument: false, annualDays: 5, color: "#123456", sortOrder: 20, isActive: true });
    const t2 = await admin.put(`/api/v1/leave/types/${t.id}`, { name: `مرخصی QA ${sfx}`, kind: "other", paid: true, needsDocument: true, annualDays: 6, color: "#123456", sortOrder: 20, isActive: false });
    eq(t2.isActive, false, "deactivated"); eq(t2.annualDays, 6, "updated");
    const del = await qa.anon("DELETE", `/api/v1/leave/types/${t.id}`, undefined, { token: admin.token });
    check([404, 405].includes(del.status), `no delete endpoint (got ${del.status})`);
    await writer.refused("POST", "/api/v1/leave/types", { name: "x", kind: "other", paid: false, needsDocument: false, annualDays: null, color: "#000000", sortOrder: 1, isActive: true }, [403]);
  });

  await rec.step("2.12.g", "asset categories: create, duplicate → 409, rename/deactivate (no delete/restore endpoint — see findings)", async () => {
    const c = await admin.post("/api/v1/asset-categories", { name: `دسته QA ${sfx}`, sortOrder: 9, isActive: true });
    await admin.refused("POST", "/api/v1/asset-categories", { name: `دسته QA ${sfx}`, sortOrder: 9, isActive: true }, [409]);
    const c2 = await admin.put(`/api/v1/asset-categories/${c.id}`, { name: `دسته QA ${sfx} ۲`, sortOrder: 9, isActive: false });
    eq(c2.isActive, false, "deactivated");
    await writer.refused("POST", "/api/v1/asset-categories", { name: "x", sortOrder: 1, isActive: true }, [403]);
  });

  await rec.step("2.12.h", "help articles: create, update, delete (no restore endpoint — see findings); writer can't edit", async () => {
    const body = (over = {}) => ({ title: `راهنمای QA ${sfx}`, slug: null, category: "QA", audience: "staff", pageKey: `qa-${sfx}`, contentJson: doc("متن راهنمای آزمایشی"), sortOrder: 99, isPublished: true, ...over });
    await admin.refused("POST", "/api/v1/help", body({ title: "" }), [422]);
    const a = await admin.post("/api/v1/help", body());
    const a2 = await admin.put(`/api/v1/help/${a.id}`, body({ title: `راهنمای QA ${sfx} ۲` }));
    check(a2.title.endsWith("۲"), "updated");
    await writer.refused("POST", "/api/v1/help", body({ title: "x" }), [403]);
    await admin.del(`/api/v1/help/${a.id}`);
    eq((await admin.get(`/api/v1/help?q=${encodeURIComponent(`QA ${sfx}`)}`)).length, 0, "deleted article hidden");
    const restore = await qa.anon("POST", `/api/v1/help/${a.id}/restore`, {}, { token: admin.token });
    check([404, 405].includes(restore.status), `no restore endpoint (got ${restore.status})`);
  });

  // ---------------- live roles ----------------
  let role, user, userPhone;
  await rec.step("2.12.i", "new role with chosen permissions → assigned to a user → next token and /me reflect it", async () => {
    const cat = await admin.get("/api/v1/permissions");
    check(cat.some((p) => p.code === "crm.leads"), "permission catalog");
    await admin.refused("POST", "/api/v1/roles", { name: { fa: "بد" }, description: null, permissions: [{ code: "no.such", scope: "all" }], version: null }, [422]);
    role = await admin.post("/api/v1/roles", { name: { fa: `نقش QA ${sfx}` }, description: "QA", permissions: [{ code: "crm.leads", scope: "own" }], version: null });
    userPhone = mobile("0915");
    user = await admin.post("/api/v1/users", { displayName: `کاربر نقش ${sfx}`, phone: userPhone, roleIds: [], isActive: true });
    const before = await qa.login(userPhone);
    eq(Object.keys((await before.get("/api/v1/me")).permissions).filter((k) => k === "crm.leads").length, 0, "no crm before");
    await before.refused("GET", "/api/v1/crm/leads", undefined, [403]);
    await admin.put(`/api/v1/users/${user.id}`, { displayName: user.displayName, phone: userPhone, roleIds: [role.id], isActive: true });
    eq((await before.get("/api/v1/me")).permissions["crm.leads"], "own", "/me (read from the database) shows the new permission at once");
    const after = await qa.login(userPhone);
    await after.get("/api/v1/crm/leads"); // allowed with the next token
  });

  await rec.step("2.12.j", "role edited (add cctv.view) and soft-deleted/restored → the member's next token follows each change", async () => {
    need(role && user, "role + user");
    const r = await admin.get(`/api/v1/roles/${role.id}`);
    await admin.put(`/api/v1/roles/${role.id}`, { name: r.nameValues, description: "QA", permissions: [{ code: "crm.leads", scope: "own" }, { code: "cctv.view", scope: "all" }], version: r.version });
    await admin.refused("PUT", `/api/v1/roles/${role.id}`, { name: r.nameValues, description: "stale", permissions: [], version: r.version }, [409]);
    let u = await qa.login(userPhone);
    await u.get("/api/v1/cctv");
    await admin.del(`/api/v1/roles/${role.id}`);
    u = await qa.login(userPhone);
    await u.refused("GET", "/api/v1/crm/leads", undefined, [403]);
    await admin.post(`/api/v1/roles/${role.id}/restore`);
    u = await qa.login(userPhone);
    await u.get("/api/v1/crm/leads");
    // System roles can't be deleted.
    const sys = (await admin.get("/api/v1/roles?limit=50")).items.find((x) => x.isSystem);
    await admin.refused("DELETE", `/api/v1/roles/${sys.id}`, undefined, [409]);
  });

  await rec.step("2.12.k", "SECURITY: a role manager can't raise the permissions of a role they hold themselves", async () => {
    // A user whose only power is roles.manage edits their own role to add users.manage/settings.manage.
    const mgrRole = await admin.post("/api/v1/roles", { name: { fa: `مدیر نقش ${sfx}` }, description: null, permissions: [{ code: "roles.manage", scope: "all" }], version: null });
    const phone = mobile("0916");
    await admin.post("/api/v1/users", { displayName: `مدیر نقش ${sfx}`, phone, roleIds: [mgrRole.id], isActive: true });
    const m = await qa.login(phone);
    const r = await m.get(`/api/v1/roles/${mgrRole.id}`);
    const res = await qa.anon("PUT", `/api/v1/roles/${mgrRole.id}`, { name: r.nameValues, description: null, permissions: [{ code: "roles.manage", scope: "all" }, { code: "users.manage", scope: "all" }, { code: "settings.manage", scope: "all" }], version: r.version }, { token: m.token });
    const now = await (await qa.login(phone)).get("/api/v1/me");
    // Clean up the escalation either way, then judge.
    const cur = await admin.get(`/api/v1/roles/${mgrRole.id}`);
    await admin.put(`/api/v1/roles/${mgrRole.id}`, { name: cur.nameValues, description: null, permissions: [{ code: "roles.manage", scope: "all" }], version: cur.version });
    check(res.status === 403 && !now.permissions["users.manage"], `escalation possible: PUT own role → ${res.status}; user now has users.manage=${now.permissions["users.manage"]}`);
  });

  // ---------------- language ----------------
  await rec.step("2.12.l", "English language: listed as LTR; admin overrides an English text; user switches to English (LTR) and back", async () => {
    const langs = (await qa.anon("GET", "/api/v1/i18n/languages")).data;
    const en = langs.find((l) => l.code === "en");
    check(en, "English available"); eq(en.direction, "ltr", "English is LTR");
    const key = "qa.d.hello";
    await admin.put(`/api/v1/translations/en/${key}`, { value: `Hello QA ${sfx}` });
    const bundle = (await qa.anon("GET", "/api/v1/i18n/en")).data;
    eq(bundle.texts[key], `Hello QA ${sfx}`, "override served in the English bundle");
    await admin.refused("PUT", "/api/v1/translations/en/bad key!", { value: "x" }, [422, 404, 400]);
    await writer.refused("PUT", `/api/v1/translations/en/${key}`, { value: "x" }, [403]);
    const u = await qa.login(DEMO.writer);
    await u.refused("PATCH", "/api/v1/me", { displayName: null, languageCode: "xx" }, [422]);
    const meEn = await u.patch("/api/v1/me", { displayName: null, languageCode: "en" });
    eq(meEn.languageCode, "en", "switched to English");
    eq((await u.get("/api/v1/me")).languageCode, "en", "kept");
    await u.patch("/api/v1/me", { displayName: null, languageCode: "fa" });
    await admin.del(`/api/v1/translations/en/${key}`);
  });

  await rec.step("2.12.m", "PROMPT R13 'add a new language from the panel': an API to add/activate a language exists", async () => {
    const spec = await (await fetch(`${process.env.QA_API}/openapi/v1.json`)).json();
    const langWrite = Object.entries(spec.paths).some(([p, ops]) => /language/i.test(p) && (ops.post || ops.put));
    check(langWrite, "no endpoint creates/activates a language (only GET /api/v1/i18n/languages); English is pre-seeded");
  });

  // ---------------- brand ----------------
  await rec.step("2.12.n", "brand colours: invalid → 422; no permission → 403; saved → /public/brand shows them; restored", async () => {
    const before = (await qa.anon("GET", "/api/v1/public/brand")).data;
    await admin.refused("PUT", "/api/v1/settings/brand", { name: null, tagline: null, colors: { primary: "yellow", dark: "#000000", emphasis: null }, version: before.version || null }, [422]);
    await writer.refused("PUT", "/api/v1/settings/brand", { name: null, tagline: null, colors: { primary: "#FEFF00", dark: "#0A0A0A", emphasis: null }, version: null }, [403]);
    const saved = await admin.put("/api/v1/settings/brand", { name: `آژانس QA ${sfx}`, tagline: "شعار آزمایشی", colors: { primary: "#feff00", dark: "#0a0a0a", emphasis: "#ff3366" }, version: before.version || null });
    eq(saved.colors.primary, "#FEFF00", "stored upper-case");
    const pub = (await qa.anon("GET", "/api/v1/public/brand")).data;
    eq(pub.colors.primary, "#FEFF00", "public primary"); eq(pub.colors.dark, "#0A0A0A", "public dark"); eq(pub.name, `آژانس QA ${sfx}`, "public name");
    await admin.put("/api/v1/settings/brand", { name: before.name, tagline: before.tagline, colors: before.colors, version: pub.version });
  });

  // Not checked: PROMPT2 §1 asked for black #0A0A0A + yellow #FEFF00 defaults, but the UI redesign (PROJECT_MAP "رنگ و لوگو")
  // deliberately moved to navy #0B1B34 + royal blue #3B6CFF; BrandDefaults follows that decision (see findings D.md).

  // ---------------- setup ----------------
  await rec.step("2.12.o", "setup can't be re-run on this install: status says done; POST /setup refused", async () => {
    eq((await qa.anon("GET", "/api/v1/setup/status")).data.needsSetup, false, "setup done");
    const r = await qa.anon("POST", "/api/v1/setup", { token: "wrong-token-qa", phone: "09120000000", displayName: "هکر", brandName: null });
    check([403, 409].includes(r.status), `refused (got ${r.status})`);
    const e = await qa.anon("POST", "/api/v1/setup", { token: "", phone: "", displayName: "", brandName: null });
    check([403, 409, 422].includes(e.status), `empty body refused (got ${e.status})`);
  });
}
