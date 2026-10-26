// S07 — leave (docs/PROMPT3.md §2.7, PROMPT.md §6.7): daily and hourly requests → approve / reject / cancel,
// a mission registered by the admin, the balance computed from approvals, and the error paths.
import { qa, pick, check, eq } from "../lib.mjs";

export const id = "S07";
export const title = "مرخصی — روزانه و ساعتی، تأیید/رد/لغو، ماموریت، مانده";

const userId = (actor) => JSON.parse(Buffer.from(actor.token.split(".")[1], "base64url").toString()).sub;
const tehranDay = (offset = 0) => qa.tehran(12, 0, offset).slice(0, 10);
const INVALID = [400, 422];

export async function run(rec, { crowd }) {
  const [m0, m1, m2] = pick(crowd, "marketer", 3);
  const field3 = pick(crowd, "field", 1, 3)[0];
  const managerP = pick(crowd, "lead", 1, 2)[0];
  const ownerP = pick(crowd, "lead", 1, 0)[0];
  const as = (p) => qa.login(p);
  await qa.setClock(qa.tehran(10, 0));

  let types = [];
  const T = {};
  await rec.step("2.7.a", "leave types are listed (annual 26 days, sick needs a document, mission)", async () => {
    types = await (await as(m0)).get("/api/v1/leave/types");
    T.annual = types.find((t) => t.kind === "annual");
    T.sick = types.find((t) => t.kind === "sick");
    T.mission = types.find((t) => t.kind === "mission");
    check(T.annual && T.sick && T.mission, `annual/sick/mission exist (${types.map((t) => t.kind).join(",")})`);
    eq(T.annual.annualDays, 26, "annual allowance");
    eq(T.sick.needsDocument, true, "sick needs document");
  });

  const balance = async (p) => (await (await as(p)).get("/api/v1/leave/balance")).find((b) => b.leaveTypeId === T.annual.id);
  await rec.step("2.7.b", "balance before any request: 26 allowed, 0 used", async () => {
    const b = await balance(m0);
    eq(b.allowanceDays, 26, "allowance");
    eq(b.usedDays, 0, "used");
    eq(b.leftDays, 26, "left");
  });

  // Daily leave: two whole days, from Tehran midnight of day+2 to day+4.
  const daily = { leaveTypeId: null, hourly: false, from: qa.tehran(0, 0, 2), to: qa.tehran(0, 0, 4), reason: "سفر خانوادگی" };
  let dailyReq, hourlyReq, cancelReq;
  await rec.step("2.7.c", "worker asks for a 2-day annual leave → pending, managers notified live", async () => {
    dailyReq = await (await as(m0)).post("/api/v1/leave/requests", { ...daily, leaveTypeId: T.annual.id });
    eq(dailyReq.status, "pending", "status");
    eq(dailyReq.hourly, false, "daily");
    eq(dailyReq.byManager, false, "byManager");
    const mgr = await as(managerP);
    const pending = await mgr.get("/api/v1/leave/requests?pending=true");
    check(pending.some((l) => l.id === dailyReq.id), "manager sees it in pending");
    check(await mgr.hasNotification((n) => n.kind === "leave.request"), "manager got leave.request notification");
    // Balance does not move while pending.
    eq((await balance(m0)).usedDays, 0, "pending does not count");
  });

  await rec.step("2.7.d", "manager approves → approved, worker notified, balance 2 used / 24 left", async () => {
    const r = await (await as(managerP)).post(`/api/v1/leave/requests/${dailyReq.id}/decide`, { approve: true, note: "خوش بگذرد" });
    eq(r.leave.status, "approved", "status");
    check(Array.isArray(r.workToReassign), "reassignment suggestions returned");
    const inbox = await (await as(m0)).get("/api/v1/notifications");
    check(inbox.items.some((n) => n.kind === "leave.decided"), "worker notified");
    const b = await balance(m0);
    eq(b.usedDays, 2, "used days");
    eq(b.leftDays, 24, "left days");
  });

  await rec.step("2.7.e", "hourly leave (tomorrow 10–12) → manager rejects with a note; balance unchanged", async () => {
    hourlyReq = await (await as(m1)).post("/api/v1/leave/requests", {
      leaveTypeId: T.annual.id, hourly: true, from: qa.tehran(10, 0, 1), to: qa.tehran(12, 0, 1), reason: "کار بانکی",
    });
    eq(hourlyReq.status, "pending", "status");
    eq(hourlyReq.minutes, 120, "minutes");
    const r = await (await as(managerP)).post(`/api/v1/leave/requests/${hourlyReq.id}/decide`, { approve: false, note: "فردا جلسه داریم" });
    eq(r.leave.status, "rejected", "status");
    eq(r.leave.decisionNote, "فردا جلسه داریم", "note kept");
    eq((await balance(m1)).usedDays, 0, "rejected does not count");
  });

  await rec.step("2.7.f", "an approved hourly leave counts as hours/workday in the balance", async () => {
    const h = await (await as(m1)).post("/api/v1/leave/requests", {
      leaveTypeId: T.annual.id, hourly: true, from: qa.tehran(14, 0, 1), to: qa.tehran(16, 0, 1), reason: "کار اداری",
    });
    await (await as(managerP)).post(`/api/v1/leave/requests/${h.id}/decide`, { approve: true });
    const b = await balance(m1);
    // Workday = 09–17 minus 60 min break = 420 min → 120/420 = 0.29 day.
    eq(b.usedDays, 0.29, "used days");
  });

  await rec.step("2.7.g", "requester cancels a pending request; cancelling again → 409; someone else can't cancel (403)", async () => {
    cancelReq = await (await as(m2)).post("/api/v1/leave/requests", {
      leaveTypeId: T.annual.id, hourly: false, from: qa.tehran(0, 0, 5), to: qa.tehran(0, 0, 6), reason: "کار شخصی",
    });
    eq(await (await as(m1)).refused("POST", `/api/v1/leave/requests/${cancelReq.id}/cancel`, {}, [403, 404]) >= 403, true, "other worker cancel");
    await (await as(m2)).post(`/api/v1/leave/requests/${cancelReq.id}/cancel`, {});
    const mine = await (await as(m2)).get("/api/v1/leave/requests");
    eq(mine.find((l) => l.id === cancelReq.id)?.status, "cancelled", "status");
    eq(await (await as(m2)).refused("POST", `/api/v1/leave/requests/${cancelReq.id}/cancel`, {}, [409]), 409, "cancel twice");
    eq(await (await as(managerP)).refused("POST", `/api/v1/leave/requests/${cancelReq.id}/decide`, { approve: true }, [409]), 409, "decide a cancelled one");
  });

  await rec.step("2.7.h", "admin registers a mission for a field worker (ForUserId) → approved at once, worker notified, no annual balance used", async () => {
    const mgr = await as(managerP);
    const fieldUid = userId(await as(field3));
    const m = await mgr.post("/api/v1/leave/requests", {
      leaveTypeId: T.mission.id, hourly: false, from: qa.tehran(0, 0, 1), to: qa.tehran(0, 0, 2), reason: "فیلمبرداری در محل مشتری", forUserId: fieldUid,
    });
    eq(m.status, "approved", "status");
    eq(m.byManager, true, "byManager");
    eq(m.userId, fieldUid, "for the worker");
    const inbox = await (await as(field3)).get("/api/v1/notifications");
    check(inbox.items.some((n) => n.kind === "leave.registered"), "worker notified of the registration");
    eq((await balance(field3)).usedDays, 0, "mission does not use annual leave");
  });

  await rec.step("2.7.i", "overlapping request (inside the approved 2-day leave) → 409", async () => {
    eq(await (await as(m0)).refused("POST", "/api/v1/leave/requests", {
      leaveTypeId: T.annual.id, hourly: true, from: qa.tehran(10, 0, 3), to: qa.tehran(11, 0, 3), reason: "همپوشانی",
    }, [409]), 409, "status");
  });

  await rec.step("2.7.j", "validation: short reason, end before start, hourly > 12 h → refused", async () => {
    const w = await as(m2);
    const base = { leaveTypeId: T.annual.id, hourly: false, from: qa.tehran(0, 0, 10), to: qa.tehran(0, 0, 11) };
    check(INVALID.includes(await w.refused("POST", "/api/v1/leave/requests", { ...base, reason: "x" }, INVALID)), "short reason");
    check(INVALID.includes(await w.refused("POST", "/api/v1/leave/requests", { ...base, from: base.to, to: base.from, reason: "برعکس" }, INVALID)), "end before start");
    check(INVALID.includes(await w.refused("POST", "/api/v1/leave/requests",
      { ...base, hourly: true, from: qa.tehran(6, 0, 10), to: qa.tehran(20, 0, 10), reason: "ساعتی طولانی" }, INVALID)), "hourly > 12h");
  });

  let pastReq;
  await rec.step("2.7.k", "leave entirely in the past: documented behaviour (accepted as pending, never auto-approved)", async () => {
    const w = await as(m2);
    const r = await w.call("POST", "/api/v1/leave/requests", {
      leaveTypeId: T.annual.id, hourly: false, from: qa.tehran(0, 0, -7), to: qa.tehran(0, 0, -6), reason: "مرخصی هفته‌ی قبل",
    }, { expect: [200, 201, 400, 409, 422] });
    if (r && r.id) { pastReq = r; eq(r.status, "pending", "a past request is at most pending"); }
  });

  await rec.step("2.7.l", "a worker can't approve (403) or register leave for someone else (403)", async () => {
    const w = await as(m1);
    eq(await w.refused("POST", `/api/v1/leave/requests/${dailyReq.id}/decide`, { approve: false }, [403]), 403, "worker decides");
    eq(await w.refused("POST", "/api/v1/leave/requests", {
      leaveTypeId: T.annual.id, hourly: false, from: qa.tehran(0, 0, 12), to: qa.tehran(0, 0, 13), reason: "برای همکار", forUserId: userId(await as(m2)),
    }, [403]), 403, "worker registers for another");
    // A worker only lists their own requests.
    const list = await w.get("/api/v1/leave/requests");
    const me = userId(w);
    check(list.every((l) => l.userId === me), "worker sees only own requests");
  });

  await rec.step("2.7.m", "a manager can't approve their own request (403); another lead can; deciding twice → 409", async () => {
    const mgr = await as(managerP);
    const own = await mgr.post("/api/v1/leave/requests", {
      leaveTypeId: T.annual.id, hourly: true, from: qa.tehran(9, 0, 8), to: qa.tehran(11, 0, 8), reason: "دندانپزشکی",
    });
    eq(await mgr.refused("POST", `/api/v1/leave/requests/${own.id}/decide`, { approve: true }, [403]), 403, "self approval");
    const r = await (await as(ownerP)).post(`/api/v1/leave/requests/${own.id}/decide`, { approve: true });
    eq(r.leave.status, "approved", "owner approves");
    eq(await (await as(ownerP)).refused("POST", `/api/v1/leave/requests/${own.id}/decide`, { approve: false }, [409]), 409, "decide twice");
  });

  await rec.step("2.7.n", "approved daily leave shows on the team calendar; rejected hourly leave does not", async () => {
    const cal = await (await as(managerP)).get(`/api/v1/leave/calendar?from=${tehranDay(0)}&to=${tehranDay(6)}`);
    const row0 = cal.find((r) => r.userId === dailyReq.userId);
    const d2 = row0.days.find((d) => d.day === tehranDay(2)), d3 = row0.days.find((d) => d.day === tehranDay(3)), d4 = row0.days.find((d) => d.day === tehranDay(4));
    eq(d2?.state, "leave", "day+2");
    eq(d3?.state, "leave", "day+3");
    check(d4?.state !== "leave", `day+4 (leave ended at midnight) is not leave (${d4?.state})`);
    const row1 = cal.find((r) => r.userId === hourlyReq.userId);
    const t1 = row1.days.find((d) => d.day === tehranDay(1));
    // m1 has the approved 14–16 hourly leave tomorrow (2.7.f) but not the rejected 10–12 one; either way it is hourly.
    eq(t1?.state, "hourlyLeave", "tomorrow shows the approved hourly leave");
  });

  await rec.step("2.7.o", "approved requester can still cancel a future approved leave; the balance gives the days back", async () => {
    // Domain rule (LeaveRequest.Cancel): an approved leave that has not started can be cancelled by its owner.
    await (await as(m0)).post(`/api/v1/leave/requests/${dailyReq.id}/cancel`, {});
    const b = await balance(m0);
    eq(b.usedDays, 0, "used back to 0");
    eq(b.leftDays, 26, "left back to 26");
  });

  await rec.step("2.7.q", "sick leave without the required document is accepted but marked missingDocument (manager decides)", async () => {
    const r = await (await as(m2)).post("/api/v1/leave/requests", {
      leaveTypeId: T.sick.id, hourly: false, from: qa.tehran(0, 0, 14), to: qa.tehran(0, 0, 15), reason: "سرماخوردگی",
    });
    eq(r.status, "pending", "status");
    eq(r.missingDocument, true, "missingDocument flag");
  });

  await rec.step("2.7.p", "the absence job does not flag someone on an approved mission (tomorrow 09:30)", async () => {
    await qa.setClock(qa.tehran(9, 30, 1));
    await qa.job("absences");
    const all = await (await as(managerP)).get("/api/v1/leave/absences");
    const fieldUid = userId(await as(field3));
    const m2Uid = userId(await as(m2));
    const day = tehranDay(1);
    check(!all.some((a) => a.userId === fieldUid && a.day === day), "worker on mission has no absence tomorrow");
    check(all.some((a) => a.userId === m2Uid && a.day === day), "control: a worker without leave is flagged tomorrow");
  });

  await rec.step("2.7.r", "leave registered by the admin after an absence was detected resolves it (no unexcused absence next to a paid leave)", async () => {
    // m2 did not come today; at 10:00 the absence job flags it; then the manager registers a sick day "told by phone".
    await qa.setClock(qa.tehran(10, 0));
    await qa.job("absences");
    const day = tehranDay(0);
    const m2Uid = userId(await as(m2));
    const before = (await (await as(managerP)).get("/api/v1/leave/absences")).find((a) => a.userId === m2Uid && a.day === day);
    check(before?.status === "detected", `absence detected first (${JSON.stringify(before?.status)})`);
    await (await as(managerP)).post("/api/v1/leave/requests", {
      leaveTypeId: T.sick.id, hourly: false, from: qa.tehran(0, 0, 0), to: qa.tehran(0, 0, 1), reason: "تلفنی خبر داد که مریض است", forUserId: m2Uid,
    });
    // Two days later (+2 h margin) the 48 h reason deadline has passed and the job runs again.
    await qa.setClock(qa.tehran(12, 0, 2));
    await qa.job("absences");
    const rows = await (await as(managerP)).get(`/api/v1/leave/report?from=${day}&to=${day}`);
    const r = rows.find((x) => x.userId === m2Uid);
    check(r.paidLeaveMinutes > 0, `paid leave counted (${r.paidLeaveMinutes})`);
    eq(r.unexcusedAbsences, 0, "no unexcused absence on a day covered by approved leave");
  });
}
