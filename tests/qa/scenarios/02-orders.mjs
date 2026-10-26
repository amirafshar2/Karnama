// S02 — Orders (docs/PROMPT3.md §2.2): catalog + dynamic forms, accept + suggested assignee, timers, resumable upload
// with a dropped connection, review loop, client approval / "اصلاح لازم است", publishing (agency, client reminder, overdue),
// and the error/cancel paths (bad form data, no permission, double accept, cancel, no quota left).
import http from "node:http";
import { createHash, randomBytes } from "node:crypto";
import { qa, pick, key, check, eq, API, HttpError } from "../lib.mjs";

export const id = "S02";
export const title = "سفارش‌ها: کاتالوگ → پذیرش → تایمر → آپلود → بازبینی → تأیید مشتری → انتشار";

// Crowd members this scenario uses (kept apart from S10's people):
//   lead[2] = manager (accepts, assigns, reviews, publishes) · office[0] = video editor (no orders.manage)
//   client[2], client[3] = agency-managed pages · client[27], client[28] = client-managed pages
//   client[4] = quota exhaustion · client[5] = cancel path
const SINGLE_POST = "پست تکی";      // 1 stage, photographer role, admin review + client approval, 48h SLA
const SLIDE_POST = "پست اسلایدی";   // 2 stages, quota 4 on the cheapest FullAccess package
const INFLUENCER = "کمپین اینفلوئنسر"; // in no package: always "outside package"

async function cardOf(actor, workId) { return (await actor.get(`/api/v1/work/${workId}`)).card; }
async function move(actor, workId, status, note) {
  const c = await cardOf(actor, workId);
  return actor.post(`/api/v1/work/${workId}/move`, { status, version: c.version, note });
}
/** Access tokens live 15 minutes on the QA clock: moving the clock means signing everyone in again (same person, same IP). */
const signedIn = [];
async function login(person, opts) { const a = await qa.login(person, opts); signedIn.push(a); return a; }
async function advance(minutes) {
  await qa.advance(minutes);
  for (const a of signedIn) a.token = (await qa.login(a.person)).token;
}

/** One PATCH that dies half-way (the "net drops" case): sends `sent` of `declared` bytes, then kills the socket. */
function droppedPatch(token, fileId, offset, bytes, declared) {
  return new Promise((resolve) => {
    const u = new URL(`${API}/api/v1/files/${fileId}/upload`);
    const req = http.request({ hostname: u.hostname, port: u.port, path: u.pathname, method: "PATCH",
      headers: { Authorization: `Bearer ${token}`, "Upload-Offset": String(offset), "Content-Type": "application/offset+octet-stream", "Content-Length": String(declared) } });
    req.on("error", () => resolve("dropped"));
    req.on("response", (res) => { res.resume(); resolve(`answered ${res.statusCode}`); });
    req.write(bytes, () => setTimeout(() => { req.destroy(); resolve("dropped"); }, 150));
  });
}
async function patch(actor, fileId, offset, bytes, expect = [200]) {
  const res = await fetch(`${API}/api/v1/files/${fileId}/upload`, { method: "PATCH", body: bytes,
    headers: { Authorization: `Bearer ${actor.token}`, "Upload-Offset": String(offset), "Content-Type": "application/offset+octet-stream" } });
  const text = await res.text(); let data = text; try { data = JSON.parse(text); } catch { /* not JSON */ }
  if (!expect.includes(res.status)) throw new HttpError(res.status, data, "PATCH", `/api/v1/files/${fileId}/upload`);
  return { status: res.status, data };
}

export async function run(rec, { crowd }) {
  signedIn.length = 0;
  await qa.setClock(qa.tehran(9, 0));
  const manager = await login(pick(crowd, "lead", 1, 2)[0]);
  const editor = await login(pick(crowd, "office", 1, 0)[0]);
  const [cA, cB] = await Promise.all(pick(crowd, "client", 2, 2).map((p) => login(p)));
  const [cC, cD] = await Promise.all(pick(crowd, "client", 2, 27).map((p) => login(p)));
  const cQuota = await login(pick(crowd, "client", 1, 4)[0]);
  const cCancel = await login(pick(crowd, "client", 1, 5)[0]);
  const s = {};

  // ---------------- catalog & dynamic forms ----------------
  await rec.step("2.2.a", "مشتری کاتالوگ و فرم دینامیک خدمت را می‌بیند", async () => {
    const options = await cA.get("/api/v1/services/options");
    check(Array.isArray(options) && options.length >= 3, "catalog has services");
    s.svc = Object.fromEntries(options.map((o) => [o.name, o.id]));
    for (const n of [SINGLE_POST, SLIDE_POST, INFLUENCER]) check(s.svc[n], `service ${n} in catalog`);
    const form = await cA.get(`/api/v1/services/${s.svc[SINGLE_POST]}/form`);
    const topic = form.fields.find((f) => f.key === "topic");
    check(topic?.required === true, "topic is a required field");
    check(form.fields.find((f) => f.key === "mood")?.options?.length === 3, "mood is a select with 3 options");
    s.formVersion = form.version;
  });

  await rec.step("2.2.b", "داده‌ی نامعتبر فرم → 422 (الزامی خالی، گزینه‌ی ناموجود، تاریخ غلط، نوع غلط)", async () => {
    const base = { serviceTypeId: s.svc[SINGLE_POST], priority: "normal", autoAccept: false };
    const bad = [
      { answers: {} },                                              // required topic missing
      { answers: { topic: "   " } },                                // whitespace only
      { answers: { topic: "ok", mood: "angry" } },                  // not one of the options
      { answers: { topic: "ok", publishAt: "فردا عصر" } },          // not a date
      { answers: { topic: 42 } },                                   // wrong JSON type
      { answers: { topic: "x".repeat(501) } },                      // too long for a text field
    ];
    for (const b of bad) eq(await cA.refused("POST", "/api/v1/portal/orders", { ...base, ...b }, [422]), 422, `bad form ${JSON.stringify(b.answers).slice(0, 40)}`);
    const home = await cA.get("/api/v1/portal/home");
    eq(home.orders.length, 0, "no order was created by the rejected forms");
  });

  await rec.step("2.2.c", "مشتری‌ها با فرم معتبر سفارش می‌دهند (اولویت مشتری همیشه عادی)", async () => {
    const order = (c, topic, extra = {}) => c.post("/api/v1/portal/orders", { serviceTypeId: s.svc[SINGLE_POST], title: topic, priority: "urgent", autoAccept: true,
      answers: { topic, mood: "friendly", publishAt: new Date(Date.now() + 3 * 86400_000).toISOString(), unknownField: "<script>" }, ...extra });
    s.oA1 = await order(cA, "معرفی محصول پاییزی");
    s.oA2 = await order(cA, "تخفیف آخر هفته");
    s.oB1 = await order(cB, "افتتاح شعبه‌ی دوم");
    for (const o of [s.oA1, s.oA2, s.oB1]) {
      eq(o.status, "submitted", "client order waits for the agency (autoAccept ignored)");
      eq(o.source, "client", "source");
      eq(o.priority, "normal", "client cannot set priority");
      eq(o.price, null, "client never sees the price");
      check(!o.answers.some((a) => a.key === "unknownField"), "unknown form keys are dropped");
    }
    eq(s.oA1.answers.find((a) => a.key === "mood")?.value, "friendly", "answer stored");
    const notes = await manager.get("/api/v1/notifications");
    check(notes.items.some((n) => (n.title ?? "").includes(`#${s.oA1.number}`)), "manager notified of the new client order");
  });

  await rec.step("2.2.d", "کارگر بدون مجوز سفارش را بپذیرد → 403؛ مشتری B سفارش A را ببیند → 404", async () => {
    eq(await editor.refused("POST", `/api/v1/orders/${s.oA1.id}/accept?version=${s.oA1.version}`, {}, [403]), 403, "editor accept");
    eq(await cA.refused("POST", `/api/v1/orders/${s.oA1.id}/accept?version=${s.oA1.version}`, {}, [403]), 403, "client accept");
    eq(await cB.refused("GET", `/api/v1/orders/${s.oA1.id}`, undefined, [404]), 404, "other client's order");
  });

  await rec.step("2.2.e", "ادمین می‌پذیرد → WorkItem با ددلاین SLA؛ پذیرش دوباره → 409", async () => {
    const before = await manager.get(`/api/v1/orders/${s.oA1.id}`);
    s.oA1 = await manager.post(`/api/v1/orders/${s.oA1.id}/accept?version=${before.version}`);
    eq(s.oA1.status, "inProgress", "accepted");
    eq(s.oA1.outsidePackage, false, "inside the package");
    eq(s.oA1.stages.length, 1, "one work item per workflow stage");
    eq(s.oA1.stages[0].status, "todo", "first stage is todo");
    const hours = (new Date(s.oA1.stages[0].deadline) - new Date((await qa.clock()).now)) / 3600_000;
    check(Math.abs(hours - 48) < 0.2, `stage deadline = accept + 48h SLA (got ${hours.toFixed(2)}h)`);
    // stale version and fresh version: both refused, nothing duplicated
    eq(await manager.refused("POST", `/api/v1/orders/${s.oA1.id}/accept?version=${before.version}`, {}, [409]), 409, "double accept (stale version)");
    eq(await manager.refused("POST", `/api/v1/orders/${s.oA1.id}/accept?version=${s.oA1.version}`, {}, [409]), 409, "double accept (fresh version)");
    eq((await manager.get(`/api/v1/orders/${s.oA1.id}`)).stages.length, 1, "still one work item");
    s.oA2 = await manager.post(`/api/v1/orders/${s.oA2.id}/accept?version=${s.oA2.version}`);
    s.oB1 = await manager.post(`/api/v1/orders/${s.oB1.id}/accept?version=${s.oB1.version}`);
    s.wA1 = s.oA1.stages[0].id; s.wA2 = s.oA2.stages[0].id; s.wB1 = s.oB1.stages[0].id;
    const home = await cA.get("/api/v1/portal/home");
    eq(home.package.quotas.find((q) => q.serviceTypeId === s.svc[SINGLE_POST]).used, 2, "client sees 2 used of its single-post quota");
  });

  await rec.step("2.2.f", "پیشنهاد خودکار کارگر (نقش مرحله، کمترین بار) → سپردن", async () => {
    eq(await editor.refused("GET", `/api/v1/work/${s.wA1}/suggestions`, undefined, [403]), 403, "worker cannot ask for suggestions");
    const sug = await manager.get(`/api/v1/work/${s.wA1}/suggestions`);
    check(sug.length > 0 && sug[0].hasStageRole, "top suggestion has the stage's role");
    for (let i = 1; i < sug.length; i++) {
      const a = sug[i - 1], b = sug[i];
      check(a.hasStageRole > b.hasStageRole || (a.hasStageRole === b.hasStageRole && a.openMinutes <= b.openMinutes), "ordered by role, then lightest load");
    }
    const top = sug[0];
    s.w1 = crowd.find((p) => p.name === top.name);
    check(s.w1, `top suggestion (${top.name}) is a QA crowd member we can sign in as`);
    let card = await cardOf(manager, s.wA1);
    await manager.post(`/api/v1/work/${s.wA1}/assign`, { userId: top.userId, version: card.version });
    // Load balancing: the next item of the same role should go to someone else now.
    const sug2 = await manager.get(`/api/v1/work/${s.wA2}/suggestions`);
    check(sug2[0].userId !== top.userId, "after one assignment the suggestion moves to a less busy colleague");
    s.w2 = crowd.find((p) => p.name === sug2[0].name);
    card = await cardOf(manager, s.wA2);
    await manager.post(`/api/v1/work/${s.wA2}/assign`, { userId: sug2[0].userId, version: card.version });
    // w1 also gets client B's post (manager override), so w1 has two items for the timer test.
    card = await cardOf(manager, s.wB1);
    await manager.post(`/api/v1/work/${s.wB1}/assign`, { userId: top.userId, version: card.version });
    // stale version on assign → 409
    eq(await manager.refused("POST", `/api/v1/work/${s.wB1}/assign`, { userId: top.userId, version: card.version }, [409]), 409, "assign with stale version");
    s.W1 = await login(s.w1); s.W2 = await login(s.w2);
    s.W1id = top.userId;
    const notes = await s.W1.get("/api/v1/notifications");
    check(notes.items.some((n) => n.kind === "work.assigned" || (n.title ?? "").includes("سپرده")), "assignee notified");
    eq(await cA.refused("GET", `/api/v1/work/${s.wA1}`, undefined, [403, 404]) >= 403, true, "client cannot open the internal work card");
    const ord = await cA.get(`/api/v1/orders/${s.oA1.id}`);
    eq(ord.stages[0].assigneeId, null, "portal never learns who works on it");
  });

  await rec.step("2.2.f2", "سپردن کار به کسی که مجوز اجرای کار ندارد (حساب کیوسک) → 422", async () => {
    const kiosk = await qa.login(pick(crowd, "kiosk", 1, 0)[0]);
    const kioskId = (await kiosk.get("/api/v1/me")).id;
    const card = await cardOf(manager, s.wA2);
    const r = await manager.call("POST", `/api/v1/work/${s.wA2}/assign`, { userId: kioskId, version: card.version }, { expect: [200, 422] });
    if (r?.assigneeId === kioskId) {   // undo, so the rest of the scenario keeps W2 on the item
      await manager.post(`/api/v1/work/${s.wA2}/assign`, { userId: (await s.W2.get("/api/v1/me")).id, version: r.version });
    }
    check(r?.status === 422, "a staff account without work.run (the office kiosk) cannot be given work (got 200: assigned)");
  });

  // ---------------- timers ----------------
  await rec.step("2.2.g", "تایمر: شروع → مکث → کار دوم → مکث خودکار اولی (فقط یک تایمر باز)", async () => {
    const W1 = s.W1;
    eq(await W1.refused("POST", `/api/v1/attendance/timer/${s.wA2}`, {}, [404]), 404, "timer on someone else's item");
    await W1.post("/api/v1/attendance/clock-in", { idempotencyKey: key() });
    await move(W1, s.wA1, "inProgress");
    let me = await W1.post(`/api/v1/attendance/timer/${s.wA1}`);
    eq(me.timer?.workItemId, s.wA1, "timer runs on item 1");
    await advance(25);
    me = await W1.del("/api/v1/attendance/timer");
    eq(me.timer, null, "paused (stopped)");
    await advance(5);
    me = await W1.post(`/api/v1/attendance/timer/${s.wA1}`);
    eq(me.timer?.workItemId, s.wA1, "resumed item 1");
    await advance(20);
    await move(W1, s.wB1, "inProgress");
    me = await W1.post(`/api/v1/attendance/timer/${s.wB1}`);
    eq(me.timer?.workItemId, s.wB1, "second item's timer is the running one");
    const inv = (await qa.invariants()).find((i) => i.name === "one-open-timer-per-user");
    check(inv?.ok, `invariant one-open-timer-per-user: ${JSON.stringify(inv)}`);
    await advance(10);
    await W1.del("/api/v1/attendance/timer");
    const det = await manager.get(`/api/v1/reports/work/${s.W1id}?from=${me.shift.day}&to=${me.shift.day}`);
    const onA1 = det.timers.filter((t) => t.orderNumber === s.oA1.number).reduce((m, t) => m + t.minutes, 0);
    const onB1 = det.timers.filter((t) => t.orderNumber === s.oB1.number).reduce((m, t) => m + t.minutes, 0);
    eq(onA1, 45, "item 1: 25 + 20 minutes (auto-paused when item 2 started)");
    eq(onB1, 10, "item 2: 10 minutes");
  });

  // ---------------- resumable upload ----------------
  await rec.step("2.2.h", "آپلود قابل ادامه: تکه‌ی ۱ → قطع نت وسط تکه‌ی ۲ → پرسیدن offset → ادامه؛ offset غلط → 409", async () => {
    const W1 = s.W1;
    const size = 300_000;
    const bytes = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), randomBytes(size - 8)]);
    const st = await W1.post("/api/v1/files", { orderId: s.oA1.id, workItemId: s.wA1, purpose: "deliverable", fileName: "post-final.png", contentType: "image/png", size });
    eq(st.received, 0, "fresh upload");
    s.fileId = st.id;
    let r = await patch(W1, st.id, 0, bytes.subarray(0, 120_000));
    eq(r.data.received, 120_000, "part 1 stored");
    const outcome = await droppedPatch(W1.token, st.id, 120_000, bytes.subarray(120_000, 180_000), 180_000);
    check(outcome === "dropped" || outcome.startsWith("answered 4") || outcome.startsWith("answered 5"), `connection dropped (${outcome})`);
    await new Promise((res) => setTimeout(res, 300));
    const status = await W1.get(`/api/v1/files/${st.id}/upload`);
    eq(status.received, 120_000, "after the drop the server still says 120000 (nothing half-counted)");
    eq(status.complete, false, "not complete");
    eq((await patch(W1, st.id, 0, bytes.subarray(0, 1000), [409])).status, 409, "wrong offset (rewind) → 409");
    eq((await patch(W1, st.id, 180_000, bytes.subarray(180_000), [409])).status, 409, "wrong offset (skip ahead) → 409");
    eq(await s.W2.refused("GET", `/api/v1/files/${st.id}/upload`, undefined, [404]), 404, "someone else cannot continue my upload");
    r = await patch(W1, st.id, status.received, bytes.subarray(status.received));
    eq(r.data.complete, true, "resumed to the end");
    eq((await patch(W1, st.id, size, Buffer.from([1]), [409])).status, 409, "append after completion → 409");
    const res = await fetch(`${API}/api/v1/files/${st.id}`, { headers: { Authorization: `Bearer ${W1.token}` } });
    eq(res.status, 200, "download");
    const got = Buffer.from(await res.arrayBuffer());
    eq(createHash("sha256").update(got).digest("hex"), createHash("sha256").update(bytes).digest("hex"), "downloaded file is byte-identical");
    eq(res.headers.get("content-type"), "image/png", "type sniffed from the bytes");
    eq(await cA.refused("GET", `/api/v1/files/${st.id}`, undefined, [404]), 404, "client cannot fetch the deliverable before it is sent to them");
  });

  // ---------------- review loop ----------------
  await rec.step("2.2.i", "ارسال برای بازبینی → رد با یادداشت → اصلاح → تأیید → ارسال برای مشتری", async () => {
    const W1 = s.W1;
    eq(await W1.refused("POST", `/api/v1/work/${s.wA1}/move`, { status: "clientReview", version: (await cardOf(W1, s.wA1)).version }, [403]), 403, "worker cannot skip admin review");
    eq(await W1.refused("POST", `/api/v1/work/${s.wA1}/move`, { status: "done", version: (await cardOf(W1, s.wA1)).version }, [403]), 403, "worker cannot finish it himself");
    let c = await move(W1, s.wA1, "inReview");
    eq(c.status, "inReview", "submitted for review");
    c = await move(manager, s.wA1, "changesRequested", "رنگ لوگو با برند نمی‌خواند؛ نسخه‌ی تیره‌تر بگذار.");
    eq(c.status, "changesRequested", "rejected by the reviewer");
    eq(c.revisionCount, 1, "one revision counted");
    const d = await W1.get(`/api/v1/work/${s.wA1}`);
    check(d.comments.some((x) => x.body.includes("لوگو") && x.visibility === "internal"), "reviewer's note is on the card as an internal comment");
    check((await W1.get("/api/v1/notifications")).items.some((n) => n.kind === "work.changes" || (n.title ?? "").includes("اصلاح")), "worker notified about the changes");
    await move(W1, s.wA1, "inProgress");
    await move(W1, s.wA1, "inReview");
    c = await move(manager, s.wA1, "clientReview");
    eq(c.status, "clientReview", "approved by the reviewer → sent to the client");
    // client B's item goes the same way (straight through review)
    await move(W1, s.wB1, "inReview");
    await move(manager, s.wB1, "clientReview");
    const home = await cA.get("/api/v1/portal/home");
    const ap = home.approvals.find((a) => a.workItemId === s.wA1);
    check(ap, "the item waits for client A's approval");
    check(ap.files.some((f) => f.id === s.fileId), "the deliverable is now visible to the client");
    eq(home.orders.find((o) => o.id === s.oA1.id)?.stepLabel, "yourApproval", "client sees 'your approval'");
  });

  await rec.step("2.2.j", "مشتری A تأیید می‌کند؛ مشتری B «اصلاح لازم است» می‌خواهد", async () => {
    eq(await cB.refused("POST", `/api/v1/portal/approvals/${s.wA1}`, { approve: true }, [404]), 404, "client B cannot approve client A's item");
    eq(await cB.refused("POST", `/api/v1/portal/approvals/${s.wB1}`, { approve: false, note: "  " }, [422]), 422, "changes without a note → 422");
    eq(await cA.refused("POST", `/api/v1/portal/approvals/${s.wA1}`, { approve: true, stars: 9 }, [422]), 422, "stars out of range → 422");
    await cA.post(`/api/v1/portal/approvals/${s.wA1}`, { approve: true, note: "عالی شد", stars: 5 });
    const o = await manager.get(`/api/v1/orders/${s.oA1.id}`);
    eq(o.stages[0].status, "done", "approved → done");
    eq(o.status, "completed", "last stage done → order completed");
    await cB.post(`/api/v1/portal/approvals/${s.wB1}`, { approve: false, note: "اصلاح لازم است: شماره تلفن شعبه اشتباه است." });
    const d = await s.W1.get(`/api/v1/work/${s.wB1}`);
    eq(d.card.status, "changesRequested", "back to the worker");
    eq(d.card.revisionCount, 1, "client's request counts as a revision");
    check(d.comments.some((x) => x.fromClient && x.body.includes("شماره تلفن")), "client's note reaches the team");
    const msgs = await cB.get(`/api/v1/portal/orders/${s.oB1.id}/messages`);
    check(msgs.some((m) => m.fromMe && m.body.includes("شماره تلفن")), "client sees its own note in the order thread");
    check((await manager.get("/api/v1/notifications")).items.some((n) => n.kind === "portal.changes" || (n.title ?? "").includes("اصلاح خواست")), "managers told the client wants changes");
    // fix and approve, so the order closes cleanly
    await move(s.W1, s.wB1, "inProgress");
    await move(s.W1, s.wB1, "inReview");
    await move(manager, s.wB1, "clientReview");
    await cB.post(`/api/v1/portal/approvals/${s.wB1}`, { approve: true });
    eq((await manager.get(`/api/v1/orders/${s.oB1.id}`)).status, "completed", "client B's order completed after the fix");
  });

  await rec.step("2.2.j2", "تأیید دوباره‌ی کار تمام‌شده → 409 و ستاره‌ی مشتری عوض نشود", async () => {
    const r = await cA.call("POST", `/api/v1/portal/approvals/${s.wA1}`, { approve: true, stars: 1 }, { expect: [204, 409] });
    const d = await manager.get(`/api/v1/work/${s.wA1}`);
    eq(d.clientStars, 5, "client's 5 stars stay after the item is closed");
    check(r?.status === 409, "approving an already-done item is refused with 409 (got 204: silently accepted again)");
  });

  // ---------------- publishing ----------------
  await rec.step("2.2.k", "PublishJob پیج آژانس: آژانس منتشر می‌کند + لینک؛ مشتری نمی‌تواند", async () => {
    const platforms = await manager.get("/api/v1/platforms");
    s.instagram = (platforms.items ?? platforms).find((p) => p.key === "instagram")?.id;
    check(s.instagram, "instagram platform");
    const handleA = pick(crowd, "client", 1, 2)[0];
    const clientA = (await manager.get(`/api/v1/orders/${s.oA1.id}`)).clientId;
    const at = new Date(new Date((await qa.clock()).now).getTime() + 60 * 60_000).toISOString();
    eq(await manager.refused("POST", "/api/v1/publishing", { clientId: clientA, platformId: s.instagram, handle: "someone_else", publishAt: at, caption: "x" }, [422]), 422, "page not registered for the client");
    eq(await manager.refused("POST", "/api/v1/publishing", { clientId: clientA, platformId: s.instagram, handle: "qa_3", publishAt: new Date(Date.now() - 86400_000 * 3).toISOString(), caption: "x" }, [422]), 422, "publish time in the past");
    const job = await manager.post("/api/v1/publishing", { clientId: clientA, orderId: s.oA1.id, platformId: s.instagram, handle: "qa_3", publishAt: at,
      caption: "پاییز با ما 🍂", hashtags: ["#پاییز", "تخفیف", "two words"], fileIds: [s.fileId] });
    eq(job.management, "agencyManaged", `client ${handleA.name}'s page is agency-managed`);
    eq(job.status, "scheduled", "scheduled");
    eq(job.hashtags.length, 2, "hashtags cleaned (# stripped, spaces refused)");
    eq(await cA.refused("POST", `/api/v1/portal/publishing/${job.id}/published`, {}, [403]), 403, "client cannot confirm an agency page");
    eq(await manager.refused("POST", `/api/v1/publishing/${job.id}/published`, { postUrl: "javascript:alert(1)" }, [422]), 422, "post link must be https");
    const done = await manager.post(`/api/v1/publishing/${job.id}/published`, { postUrl: "https://instagram.com/p/qa3post" });
    eq(done.status, "published", "agency published");
    eq(done.publishedByClient, false, "by the agency");
    eq(await manager.refused("POST", `/api/v1/publishing/${job.id}/cancel`, {}, [409]), 409, "published job cannot be cancelled");
  });

  await rec.step("2.2.k2", "کارگری که روی سفارش این مشتری کار نمی‌کند نمی‌تواند پست را «منتشر شد» بزند", async () => {
    const clientA = (await manager.get(`/api/v1/orders/${s.oA1.id}`)).clientId;
    const at = new Date(new Date((await qa.clock()).now).getTime() + 3 * 60 * 60_000).toISOString();
    const job = await manager.post("/api/v1/publishing", { clientId: clientA, platformId: s.instagram, handle: "qa_3", publishAt: at, caption: "پست دوم" });
    eq(await editor.refused("GET", `/api/v1/publishing?from=${encodeURIComponent(new Date(Date.parse(at) - 86400_000).toISOString())}&to=${encodeURIComponent(new Date(Date.parse(at) + 86400_000).toISOString())}`, undefined, [403]), 403, "the editor cannot even list the calendar");
    const r = await editor.call("POST", `/api/v1/publishing/${job.id}/published`, { postUrl: "https://instagram.com/p/fake" }, { expect: [200, 403, 404] });
    if (r?.status === "published") await manager.post(`/api/v1/publishing/${job.id}/cancel`).catch(() => null);
    check(r?.status === 403 || r?.status === 404, `an unrelated worker (work.run:assigned) is refused (got status '${r?.status}')`);
  });

  await rec.step("2.2.l", "پیج دست مشتری: یادآوری سر ساعت (publish-tick) → «منتشر کردم»؛ دیگری Overdue", async () => {
    const clientC = (await manager.get("/api/v1/clients?q=" + encodeURIComponent(pick(crowd, "client", 1, 27)[0].name))).items[0].id;
    const clientD = (await manager.get("/api/v1/clients?q=" + encodeURIComponent(pick(crowd, "client", 1, 28)[0].name))).items[0].id;
    const now = new Date((await qa.clock()).now).getTime();
    const at = new Date(now + 30 * 60_000).toISOString();
    const jC = await manager.post("/api/v1/publishing", { clientId: clientC, platformId: s.instagram, handle: "qa_28", publishAt: at, caption: "کپشن مشتری C", hashtags: ["شیراز"] });
    const jD = await manager.post("/api/v1/publishing", { clientId: clientD, platformId: s.instagram, handle: "qa_29", publishAt: at, caption: "کپشن مشتری D" });
    eq(jC.management, "clientManaged", "client C runs its own page");
    await qa.job("publish-tick");
    eq((await cC.get("/api/v1/portal/publishing")).find((j) => j.id === jC.id)?.status, "scheduled", "not due before its time");
    await advance(31);
    await qa.job("publish-tick");
    const portalC = await cC.get("/api/v1/portal/publishing");
    eq(portalC.find((j) => j.id === jC.id)?.status, "due", "due at the scheduled time");
    check(portalC.every((j) => j.clientId === (portalC[0]?.clientId)), "client sees only its own jobs");
    const notesC = await cC.get("/api/v1/notifications");
    eq(notesC.items.filter((n) => n.kind === "publish.reminder").length, 1, "exactly one reminder to the client");
    await qa.job("publish-tick");
    eq((await cC.get("/api/v1/notifications")).items.filter((n) => n.kind === "publish.reminder").length, 1, "tick again → still one reminder");
    eq(await cD.refused("POST", `/api/v1/portal/publishing/${jC.id}/published`, {}, [404]), 404, "client D cannot confirm client C's job");
    const pub = await cC.post(`/api/v1/portal/publishing/${jC.id}/published`, { postUrl: "https://instagram.com/p/qa28" });
    eq(pub.status, "published", "client: «منتشر کردم»");
    eq(pub.publishedByClient, true, "recorded as published by the client");
    // D never confirms: 2 hours later → Overdue + managers alerted
    await advance(121);
    await qa.job("publish-tick");
    const list = await manager.get(`/api/v1/publishing?from=${encodeURIComponent(new Date(now - 86400_000).toISOString())}&to=${encodeURIComponent(new Date(now + 86400_000).toISOString())}&clientId=${clientD}`);
    eq(list.find((j) => j.id === jD.id)?.status, "overdue", "unconfirmed after 2h → overdue");
    check((await manager.get("/api/v1/notifications")).items.some((n) => n.kind === "publish.overdue"), "managers alerted about the overdue post");
    eq((await cC.get("/api/v1/portal/publishing")).find((j) => j.id === jC.id)?.status, "published", "C's job stays published (no overdue after confirmation)");
  });

  // ---------------- cancel & quota ----------------
  await rec.step("2.2.m", "لغو سفارش: مشتری لغو می‌کند؛ کارها لغو و سهمیه آزاد می‌شود", async () => {
    // The client cancels its own order from the portal while the agency hasn't accepted it yet (QA B1 fix).
    const p = await cCancel.post("/api/v1/portal/orders", { serviceTypeId: s.svc[SINGLE_POST], title: "سفارشی که مشتری لغو می‌کند", priority: "normal", autoAccept: false, answers: { topic: "لغو" } });
    eq(await cCancel.refused("POST", `/api/v1/orders/${p.id}/cancel`, {}, [403]), 403, "the staff cancel route still refuses the client");
    const own = await cCancel.post(`/api/v1/portal/orders/${p.id}/cancel`);
    eq(own.status, "cancelled", "client cancels its pending order");
    eq(await cCancel.refused("POST", `/api/v1/portal/orders/${p.id}/cancel`, {}, [409]), 409, "client cancel twice → 409");
    eq(await cQuota.refused("POST", `/api/v1/portal/orders/${p.id}/cancel`, {}, [404]), 404, "another client can't cancel it (404)");
    eq(await manager.refused("POST", `/api/v1/orders/${p.id}/accept?version=${(await manager.get(`/api/v1/orders/${p.id}`)).version}`, {}, [409]), 409, "accept a client-cancelled order → 409");
    eq((await cCancel.get("/api/v1/portal/home")).orders.find((x) => x.id === p.id)?.stepLabel, "cancelled", "client sees its cancelled order");
    check((await manager.get("/api/v1/notifications")).items.some((n) => n.kind === "order.cancelledByClient"), "managers hear about the client's cancel");

    // After acceptance the portal refuses with a clear reason; the admin cancels at the client's request.
    const o = await cCancel.post("/api/v1/portal/orders", { serviceTypeId: s.svc[SINGLE_POST], title: "سفارشی که لغو می‌شود", priority: "normal", autoAccept: false, answers: { topic: "لغو" } });
    const acc = await manager.post(`/api/v1/orders/${o.id}/accept?version=${o.version}`);
    const usedBefore = (await cCancel.get("/api/v1/portal/home")).package.quotas.find((q) => q.serviceTypeId === s.svc[SINGLE_POST]).used;
    const late = await cCancel.call("POST", `/api/v1/portal/orders/${o.id}/cancel`, {}, { expect: [409] });
    check(JSON.stringify(late).includes("به آژانس پیام بدهید"), `accepted order → 409 with a Persian reason: ${JSON.stringify(late)}`);
    const cancelled = await manager.post(`/api/v1/orders/${o.id}/cancel`);
    eq(cancelled.status, "cancelled", "order cancelled");
    eq(cancelled.stages.length, 0, "its work items are cancelled (hidden from the stage list)");
    eq(await manager.refused("POST", `/api/v1/orders/${o.id}/cancel`, {}, [409]), 409, "cancel twice → 409");
    eq(await manager.refused("POST", `/api/v1/orders/${o.id}/accept?version=${acc.version}`, {}, [409]), 409, "accept a cancelled order → 409");
    const usedAfter = (await cCancel.get("/api/v1/portal/home")).package.quotas.find((q) => q.serviceTypeId === s.svc[SINGLE_POST]).used;
    eq(usedAfter, usedBefore - 1, "the quota slot is given back");
    eq((await cCancel.get("/api/v1/portal/home")).orders.find((x) => x.id === o.id)?.stepLabel, "cancelled", "client sees 'cancelled'");
    // a submitted (not yet accepted) order can be rejected with a reason the client sees
    const o2 = await cCancel.post("/api/v1/portal/orders", { serviceTypeId: s.svc[SINGLE_POST], priority: "normal", autoAccept: false, answers: { topic: "رد" } });
    eq(await manager.refused("POST", `/api/v1/orders/${o2.id}/reject`, { reason: " " }, [422]), 422, "reject without a reason → 422");
    const rej = await manager.post(`/api/v1/orders/${o2.id}/reject`, { reason: "این ماه ظرفیت عکاسی پر است." });
    eq((await cCancel.get(`/api/v1/orders/${o2.id}`)).rejectReason, rej.rejectReason, "client sees the reject reason");
  });

  await rec.step("2.2.n", "سهمیه تمام → سفارش «خارج از پکیج» با قیمت پایه؛ خدمت بیرون از پکیج هم خارج از پکیج", async () => {
    const home = await cQuota.get("/api/v1/portal/home");
    const q = home.package.quotas.find((x) => x.serviceTypeId === s.svc[SLIDE_POST]);
    check(q && q.total > 0, "client has a slide-post quota");
    const clientId = (await cQuota.get("/api/v1/me")) && (await manager.get("/api/v1/clients?q=" + encodeURIComponent(home.clientName))).items[0].id;
    const left = q.total - q.used;
    for (let i = 0; i < left; i++) {
      const o = await manager.post("/api/v1/orders", { clientId, serviceTypeId: s.svc[SLIDE_POST], title: `اسلاید ${i + 1}`, priority: "normal", autoAccept: true, answers: { topic: `اسلاید ${i + 1}` } });
      eq(o.outsidePackage, false, `order ${i + 1} inside the package`);
    }
    // the client orders one more → accepted as extra
    const extra = await cQuota.post("/api/v1/portal/orders", { serviceTypeId: s.svc[SLIDE_POST], priority: "normal", autoAccept: false, answers: { topic: "یکی بیشتر" } });
    const acc = await manager.post(`/api/v1/orders/${extra.id}/accept?version=${extra.version}`);
    eq(acc.outsidePackage, true, "no quota left → outside the package");
    check(acc.price > 0, "extra order carries the service's base price");
    const after = (await cQuota.get("/api/v1/portal/home")).package.quotas.find((x) => x.serviceTypeId === s.svc[SLIDE_POST]);
    eq(after.used, after.total, "used never goes beyond the total");
    const inf = await cQuota.post("/api/v1/portal/orders", { serviceTypeId: s.svc[INFLUENCER], priority: "normal", autoAccept: false, answers: { topic: "کمپین" } });
    const infAcc = await manager.post(`/api/v1/orders/${inf.id}/accept?version=${inf.version}`);
    eq(infAcc.outsidePackage, true, "a service the package doesn't include → outside the package");
    const inv = (await qa.invariants()).find((i) => i.name === "quota-never-negative");
    check(inv?.ok, `invariant quota-never-negative: ${JSON.stringify(inv)}`);
    const list = await manager.get("/api/v1/orders?status=inProgress&limit=100");
    check(list.items.some((o) => o.id === extra.id && o.outsidePackage), "the board/list flags the extra order for the admin");
    // PROMPT §6.4 «با هشدار برای ادمین» (QA N1 fix): one bell notice per extra order.
    const warn = (await manager.get("/api/v1/notifications")).items.filter((n) => n.kind === "orders.outsidePackage");
    check(warn.filter((n) => n.title.includes(String(acc.number))).length === 1, `one outside-package warning for #${acc.number}`);
    check(warn.some((n) => n.title.includes(String(infAcc.number))), `warning for the influencer order #${infAcc.number}`);
  });

  await rec.step("2.2.o", "ثابت‌ها: هیچ تایمر یا شیفت تکراری؛ خروج = توقف تایمر", async () => {
    eq(await s.W1.refused("POST", `/api/v1/attendance/timer/${s.wB1}`, {}, [409]), 409, "no timer on a finished item");
    await s.W1.post(`/api/v1/attendance/timer/${s.wA2 ? s.wA2 : s.wB1}`, {}).catch(() => null); // wA2 belongs to W2 → 404, ignored
    const me = await s.W1.post("/api/v1/attendance/clock-out", { idempotencyKey: key() });
    eq(me.timer, null, "no timer after clock-out");
    const inv = await qa.invariants();
    for (const n of ["one-open-timer-per-user", "one-open-shift-per-user", "quota-never-negative"]) check(inv.find((i) => i.name === n)?.ok, `invariant ${n}`);
  });
}
