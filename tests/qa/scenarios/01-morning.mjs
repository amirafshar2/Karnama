// S01 — morning arrival (docs/PROMPT3.md §2.1): kiosk QR, on-time and late arrivals, an unannounced absence
// (reason → excused / unexcused), a home attempt, a fake-location attempt, idempotent and duplicate clock-ins.
import { qa, pick, key, check, eq, HOME_IP, OFFICE_IP } from "../lib.mjs";

export const id = "S01";
export const title = "ورود صبح — QR کیوسک، تأخیر، غیبت، خانه، مکان جعلی";

const OFFICE = { lat: 29.61, lng: 52.53 };
const userId = (actor) => JSON.parse(Buffer.from(actor.token.split(".")[1], "base64url").toString()).sub;
const tehranDay = (offset = 0) => qa.tehran(12, 0, offset).slice(0, 10);
const near = (a, b, tol = 1) => Math.abs(a - b) <= tol;

export async function run(rec, { crowd }) {
  const office = pick(crowd, "office", 16);
  const onTime = office.slice(0, 10);
  const reuser = office[10];
  const homeWorker = office[11];
  const late = office.slice(12, 14);
  const absent = office.slice(14, 16);
  const [field0, field1] = pick(crowd, "field", 2);
  const kioskP = pick(crowd, "kiosk")[0];
  const managerP = pick(crowd, "lead", 1, 2)[0];
  const day = tehranDay();
  // Tokens are validated against the QA clock, so everyone signs in again after each clock move.
  const as = (p, opts) => qa.login(p, opts);
  const clockIn = (actor, evidence, k = key()) => actor.post("/api/v1/attendance/clock-in", { idempotencyKey: k, evidence });
  const kioskCode = async () => (await (await as(kioskP)).get("/api/v1/attendance/kiosk/code")).code;

  await qa.setClock(qa.tehran(8, 50));

  await rec.step("2.1.a", "a default work schedule applies to the crowd (Sat–Wed 09:00, grace 10) so lateness is computable", async () => {
    const admin = await as(managerP);
    const setup = await admin.get("/api/v1/attendance/setup");
    const def = setup.schedules.find((s) => s.userId === null);
    check(def, "agency default schedule exists");
    check(!def.flexible, "default schedule is not flexible");
    const me = await (await as(late[0])).get("/api/v1/attendance/me");
    check(me.schedule && me.schedule.start === "09:00:00", `today's schedule for a QA worker starts 09:00 (got ${JSON.stringify(me.schedule)})`);
    eq(me.isHoliday, false, "today is a workday");
  });

  await rec.step("2.1.b", "kiosk gets a rotating QR; a worker and a kiosk login from home are refused", async () => {
    const kiosk = await as(kioskP);
    const c = await kiosk.get("/api/v1/attendance/kiosk/code");
    check(/^k1\.\d+\.\d+\.[0-9a-f]{16}$/.test(c.code), `code shape (got ${c.code})`);
    check(c.secondsLeft > 0 && c.secondsLeft <= 30, "secondsLeft in 1..30");
    eq(await (await as(onTime[0])).refused("GET", "/api/v1/attendance/kiosk/code", undefined, [403]), 403, "worker cannot mint kiosk codes");
    eq(await (await as(kioskP, { ip: HOME_IP })).refused("GET", "/api/v1/attendance/kiosk/code", undefined, [403]), 403, "kiosk from home refused");
  });

  await qa.setClock(qa.tehran(8, 55));
  let lastCode;
  const shiftIds = {};
  await rec.step("2.1.c", "10 office staff clock in at 08:55 with a fresh kiosk QR → green, no review, not late", async () => {
    for (const p of onTime) {
      lastCode = await kioskCode();
      const today = await clockIn(await as(p), { qr: lastCode });
      const s = today.shift;
      check(s, `${p.phone} has an open shift`);
      eq(s.trust, "green", `${p.phone} trust`);
      eq(s.needsReview, false, `${p.phone} needsReview`);
      eq(s.lateMinutes, 0, `${p.phone} lateMinutes`);
      check(s.methods.some((m) => m.method === "RotatingQr" && m.passed), `${p.phone} QR method passed (${JSON.stringify(s.methods)})`);
      shiftIds[p.phone] = s.id;
    }
    const next = await kioskCode();
    check(next !== lastCode, "the kiosk shows a new code once the previous one is used");
  });

  await rec.step("2.1.d", "a QR already used by someone else is flagged (red + needs review), never counted twice", async () => {
    const today = await clockIn(await as(reuser), { qr: lastCode });
    const s = today.shift;
    check(s, "shift opened (verification never blocks)");
    eq(s.trust, "red", "trust");
    eq(s.needsReview, true, "needsReview");
    check(s.methods.some((m) => m.method === "RotatingQr" && !m.passed), "QR method failed");
    const inv = (await qa.invariants()).find((i) => i.name === "kiosk-qr-used-once");
    check(inv?.ok, `invariant kiosk-qr-used-once (${JSON.stringify(inv)})`);
  });

  await rec.step("2.1.e", "a forged or stale QR from home is flagged red", async () => {
    // A photographed/forged code: the second owner tries it from home (her shift is otherwise unused in this scenario).
    const mgr = await as(pick(crowd, "lead", 1, 1)[0], { ip: HOME_IP });
    const parts = (await kioskCode()).split(".");
    const forged = `${parts[0]}.${parts[1]}.${parts[2]}.0000000000000000`;
    const s = (await clockIn(mgr, { qr: forged })).shift;
    eq(s.trust, "red", "forged QR → red");
    eq(s.needsReview, true, "forged QR → review");
    check(/QR/.test(s.reviewReason ?? ""), `reason mentions QR (${s.reviewReason})`);
  });

  await rec.step("2.1.f", "replaying the same clock-in (same idempotency key) does not open a second shift", async () => {
    const p = field1; // first clock-in for field1: real GPS inside the office geofence, from mobile data (not office IP)
    const k = key();
    const ev = { latitude: OFFICE.lat + 0.0003, longitude: OFFICE.lng, accuracy: 15, mock: false };
    const a = await as(p, { ip: HOME_IP });
    const first = await clockIn(a, ev, k);
    const second = await clockIn(a, ev, k);
    eq(second.shift?.id, first.shift?.id, "same shift returned for the replay");
    const mgr = await as(managerP);
    const shifts = await mgr.get(`/api/v1/attendance/shifts?userId=${userId(a)}&from=${day}&to=${day}`);
    eq(shifts.length, 1, "exactly one shift today");
    shiftIds[p.phone] = first.shift.id;
  });

  await rec.step("2.1.j", "field worker with a genuine GPS fix inside the office radius → green via Geofence", async () => {
    const s = (await (await as(field1)).get("/api/v1/attendance/me")).shift;
    check(s, "field1 has an open shift");
    eq(s.trust, "green", "trust");
    eq(s.needsReview, false, "needsReview");
    check(s.methods.some((m) => m.method === "Geofence" && m.passed), `geofence passed (${JSON.stringify(s.methods)})`);
    check(s.methods.some((m) => m.method === "OfficeNetwork" && !m.passed), "office network correctly failed (mobile data)");
  });

  await rec.step("2.1.g", "a second clock-in with a new key while a shift is open → 409", async () => {
    const a = await as(onTime[0]);
    eq(await a.refused("POST", "/api/v1/attendance/clock-in", { idempotencyKey: key(), evidence: null }, [409]), 409, "status");
  });

  await rec.step("2.1.h", "clock-in from home (no QR, no GPS) → accepted but yellow + needs review", async () => {
    const s = (await clockIn(await as(homeWorker, { ip: HOME_IP }), null)).shift;
    check(s, "shift opened");
    eq(s.trust, "yellow", "trust");
    eq(s.needsReview, true, "needsReview");
    check(s.methods.every((m) => !m.passed), "every method failed");
  });

  await rec.step("2.1.i", "field worker with a mock (fake) GPS location → red + needs review", async () => {
    const s = (await clockIn(await as(field0, { ip: HOME_IP }), { latitude: OFFICE.lat, longitude: OFFICE.lng, accuracy: 5, mock: true })).shift;
    eq(s.trust, "red", "trust");
    eq(s.needsReview, true, "needsReview");
    check(s.methods.some((m) => m.method === "Geofence" && !m.passed), "geofence failed");
    check((s.reviewReason ?? "").includes("جعلی"), `reason says fake location (${s.reviewReason})`);
    shiftIds[field0.phone] = s.id;
  });

  // ---- 09:20: grace (10 min) has passed → the absence job runs ----
  await qa.setClock(qa.tehran(9, 20));
  let absences = [];
  const ids = {};
  await rec.step("2.1.k", "absence job after start+grace: late and absent people get an Absence; present people do not; re-run is idempotent", async () => {
    const n = await qa.job("absences");
    check(n >= 4, `job detected at least the 4 missing QA people (count ${n})`);
    const mgr = await as(managerP);
    absences = await mgr.get("/api/v1/leave/absences?open=true");
    for (const p of [...late, ...absent]) {
      const a = await as(p);
      ids[p.phone] = userId(a);
      const mine = absences.find((x) => x.userId === ids[p.phone] && x.day === day);
      check(mine && mine.status === "detected", `${p.phone} absence detected (${JSON.stringify(mine)})`);
    }
    for (const p of [onTime[0], homeWorker, field0]) {
      const uid = userId(await as(p));
      check(!absences.some((x) => x.userId === uid && x.day === day), `${p.phone} (clocked in) has no absence`);
    }
    eq(await qa.job("absences"), 0, "second run detects nothing new");
  });

  await rec.step("2.1.k2", "the absent worker is notified (and, per PROMPT §6.7.2, the admin too)", async () => {
    const w = await (await as(absent[0])).get("/api/v1/notifications");
    check(w.items.some((n) => n.kind === "absence.detected"), "worker got absence.detected");
    const m = await (await as(managerP)).get("/api/v1/notifications");
    // The manager may get its own "you didn't clock in" notice, so look for one that names the absent worker.
    const name = absent[0].name.replace(" (QA)", "");
    check(m.items.some((n) => n.kind.startsWith("absence") && `${n.title} ${n.body ?? ""}`.includes(name)),
      `manager got an absence notification naming ${absent[0].name} (got: ${JSON.stringify(m.items.filter((n) => n.kind.startsWith("absence")).map((n) => n.title))})`);
  });

  await rec.step("2.1.l", "two late arrivals (09:25, 09:40) → lateMinutes 25 / 40 and their absence turns into lateness", async () => {
    await qa.setClock(qa.tehran(9, 25));
    let code = await kioskCode();
    const s1 = (await clockIn(await as(late[0]), { qr: code })).shift;
    await qa.setClock(qa.tehran(9, 40));
    code = await kioskCode();
    const s2 = (await clockIn(await as(late[1]), { qr: code })).shift;
    check(Math.abs(s1.lateMinutes - 25) <= 1, `first late = 25 (got ${s1.lateMinutes})`);
    check(Math.abs(s2.lateMinutes - 40) <= 1, `second late = 40 (got ${s2.lateMinutes})`);
    eq(s1.trust, "green", "late arrival with QR is still green");
    const all = await (await as(managerP)).get("/api/v1/leave/absences");
    const a1 = all.find((x) => x.userId === ids[late[0].phone] && x.day === day);
    const a2 = all.find((x) => x.userId === ids[late[1].phone] && x.day === day);
    eq(a1?.status, "becameLate", "absence 1 status");
    eq(a2?.status, "becameLate", "absence 2 status");
    check(Math.abs(a1.lateMinutes - 25) <= 1 && Math.abs(a2.lateMinutes - 40) <= 1, `absence late minutes (${a1.lateMinutes}, ${a2.lateMinutes})`);
  });

  await qa.setClock(qa.tehran(10, 30));
  const absOf = async (p) => (await (await as(p)).get("/api/v1/leave/absences")).find((x) => x.day === day);

  await rec.step("2.1.m", "absent worker A writes a reason (09–11 doctor) → manager excuses it", async () => {
    const w = await as(absent[0]);
    const a = await absOf(absent[0]);
    check(a, "worker sees own absence");
    eq(await w.refused("POST", `/api/v1/leave/absences/${a.id}/reason`, { reason: "x" }, [400, 422]) >= 400, true, "too-short reason rejected");
    const r = await w.post(`/api/v1/leave/absences/${a.id}/reason`, { reason: "دکتر رفتم، نوبت صبح", from: qa.tehran(9, 0), to: qa.tehran(11, 0) });
    eq(r.status, "reasonSubmitted", "status after reason");
    const d = await (await as(managerP)).post(`/api/v1/leave/absences/${a.id}/decide`, { approve: true, note: "موجه" });
    eq(d.status, "excused", "decided status");
  });

  await rec.step("2.1.n", "absent worker B writes a reason → manager rejects (unexcused)", async () => {
    const a = await absOf(absent[1]);
    await (await as(absent[1])).post(`/api/v1/leave/absences/${a.id}/reason`, { reason: "خواب ماندم" });
    const d = await (await as(managerP)).post(`/api/v1/leave/absences/${a.id}/decide`, { approve: false, note: "غیرموجه" });
    eq(d.status, "unexcused", "decided status");
  });

  await rec.step("2.1.o", "absence error paths: worker can't decide (403), can't write someone else's reason (404), became-late can't be decided (409)", async () => {
    const a = await absOf(absent[1]);
    const other = await as(onTime[1]);
    eq(await other.refused("POST", `/api/v1/leave/absences/${a.id}/decide`, { approve: true }, [403]), 403, "worker decides");
    eq(await other.refused("POST", `/api/v1/leave/absences/${a.id}/reason`, { reason: "دلیل جعلی" }, [404]), 404, "someone else's reason");
    const lateAbs = (await (await as(managerP)).get("/api/v1/leave/absences")).find((x) => x.userId === ids[late[0].phone] && x.day === day);
    eq(await (await as(managerP)).refused("POST", `/api/v1/leave/absences/${lateAbs.id}/decide`, { approve: false }, [409]), 409, "became-late decide");
  });

  await rec.step("2.1.p", "manager live board and review queue reflect the morning", async () => {
    const mgr = await as(managerP);
    const live = await mgr.get("/api/v1/attendance/live");
    const uidOf = async (p) => userId(await as(p));
    const st = async (p) => { const u = await uidOf(p); return live.find((x) => x.userId === u)?.state; };
    eq(await st(onTime[0]), "in", "on-time worker is in");
    eq(await st(absent[0]), "absent", "absent worker shows absent");
    const lateRow = live.find((x) => x.userId === ids[late[1].phone]);
    check(lateRow && Math.abs(lateRow.lateMinutes - 40) <= 1, `live board shows the late minutes (${lateRow?.lateMinutes})`);
    const queue = await mgr.get(`/api/v1/attendance/shifts?from=${day}&to=${day}&needsReview=true`);
    for (const p of [reuser, homeWorker, field0]) {
      const u = await uidOf(p);
      check(queue.some((s) => s.userId === u), `${p.phone} is in the review queue`);
    }
    for (const p of [onTime[0], field1]) {
      const u = await uidOf(p);
      check(!queue.some((s) => s.userId === u), `${p.phone} is not in the review queue`);
    }
  });

  await rec.step("2.1.q", "manager accepts the home shift and rejects the fake-location shift; a worker can't review", async () => {
    const mgr = await as(managerP);
    const homeUid = userId(await as(homeWorker));
    const queue = await mgr.get(`/api/v1/attendance/shifts?from=${day}&to=${day}&needsReview=true`);
    const home = queue.find((s) => s.userId === homeUid);
    const fake = queue.find((s) => s.id === shiftIds[field0.phone]);
    eq(await (await as(onTime[2])).refused("POST", `/api/v1/attendance/shifts/${home.id}/review`, { approve: true }, [403]), 403, "worker review");
    await mgr.post(`/api/v1/attendance/shifts/${home.id}/review`, { approve: true, note: "از خانه با هماهنگی" });
    await mgr.post(`/api/v1/attendance/shifts/${fake.id}/review`, { approve: false, note: "مکان جعلی" });
    const after = await mgr.get(`/api/v1/attendance/shifts?from=${day}&to=${day}`);
    const h = after.find((s) => s.id === home.id), f = after.find((s) => s.id === fake.id);
    eq(h.needsReview, false, "home shift reviewed");
    eq(f.needsReview, false, "fake shift reviewed");
    eq(f.trust, "red", "rejected shift stays red");
  });

  await rec.step("2.1.s", "monthly attendance report counts a late arrival once (40 min), not shift + absence twice", async () => {
    const rows = await (await as(managerP)).get(`/api/v1/leave/report?from=${day}&to=${day}`);
    const r = rows.find((x) => x.userId === ids[late[1].phone]);
    check(r, "late worker is in the report");
    eq(r.lateCount, 1, "late count");
    check(near(r.lateMinutes, 40), `late minutes = 40 (got ${r.lateMinutes})`);
  });

  await rec.step("2.1.t", "double-tap: 5 parallel clock-ins (different keys) open exactly one shift; the rest get 409", async () => {
    const owner = await as(pick(crowd, "lead", 1, 0)[0]);
    const res = await Promise.all(Array.from({ length: 5 }, () =>
      qa.anon("POST", "/api/v1/attendance/clock-in", { idempotencyKey: key(), evidence: null }, { token: owner.token })));
    const codes = res.map((r) => r.status).sort();
    eq(codes.filter((c) => c === 200).length, 1, `exactly one success (statuses ${codes.join(",")})`);
    check(codes.every((c) => c === 200 || c === 409), `others are 409 (statuses ${codes.join(",")})`);
    const shifts = await (await as(managerP)).get(`/api/v1/attendance/shifts?userId=${userId(owner)}&from=${day}&to=${day}`);
    eq(shifts.length, 1, "one shift");
  });

  await rec.step("2.1.t2", "offline-queue retry: 4 parallel sends with the SAME idempotency key → one shift, no 5xx", async () => {
    const w = await as(pick(crowd, "field", 1, 3)[0]);
    const k = key();
    const res = await Promise.all(Array.from({ length: 4 }, () =>
      qa.anon("POST", "/api/v1/attendance/clock-in", { idempotencyKey: k, evidence: null }, { token: w.token })));
    const codes = res.map((r) => r.status).sort();
    check(codes.every((c) => c < 500), `no server errors (statuses ${codes.join(",")})`);
    const shifts = await (await as(managerP)).get(`/api/v1/attendance/shifts?userId=${userId(w)}&from=${day}&to=${day}`);
    eq(shifts.length, 1, "one shift");
  });

  await rec.step("2.1.u", "the kiosk tablet account is not treated as an absent worker", async () => {
    const kioskUid = userId(await as(kioskP));
    const all = await (await as(managerP)).get("/api/v1/leave/absences");
    check(!all.some((a) => a.userId === kioskUid), "no absence for the kiosk account");
  });

  await rec.step("2.1.v", "an approved clock-in correction (09:25 → 08:58) also clears the lateness it caused", async () => {
    const w = await as(late[0]);
    const c = await w.post("/api/v1/attendance/corrections", { day, clockIn: qa.tehran(8, 58), reason: "۸:۵۸ رسیدم، QR کیوسک کار نکرد" });
    await (await as(managerP)).post(`/api/v1/attendance/corrections/${c.id}/decide`, { approve: true });
    const s = (await (await as(managerP)).get(`/api/v1/attendance/shifts?userId=${ids[late[0].phone]}&from=${day}&to=${day}`))[0];
    check(near((new Date(s.clockInAt) - new Date(qa.tehran(8, 58))) / 60000, 0), `clock-in corrected (${s.clockInAt})`);
    eq(s.lateMinutes, 0, "shift lateMinutes after correction");
    const r = (await (await as(managerP)).get(`/api/v1/leave/report?from=${day}&to=${day}`)).find((x) => x.userId === ids[late[0].phone]);
    eq(r.lateMinutes, 0, "report lateMinutes after correction");
  });

  await rec.step("2.1.r", "invariants after the morning (one open shift per user, kiosk QR used once)", async () => {
    const inv = await qa.invariants();
    for (const name of ["one-open-shift-per-user", "kiosk-qr-used-once"]) {
      const i = inv.find((x) => x.name === name);
      check(i?.ok, `${name}: ${JSON.stringify(i)}`);
    }
  });
}
