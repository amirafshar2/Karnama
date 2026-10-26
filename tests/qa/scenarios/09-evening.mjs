// S09 — evening (docs/PROMPT3.md §2.9, PROMPT.md §6.6): breaks, the work timer stopping at break/clock-out, early leave,
// clock-out (idempotent), a forgotten clock-out closed by the job and marked for review, and correction requests.
import { qa, pick, key, check, eq } from "../lib.mjs";

export const id = "S09";
export const title = "عصر — استراحت، تایمر، خروج، فراموشی خروج، درخواست اصلاح";

const userId = (actor) => JSON.parse(Buffer.from(actor.token.split(".")[1], "base64url").toString()).sub;
const tehranDay = (offset = 0) => qa.tehran(12, 0, offset).slice(0, 10);
const INVALID = [400, 422];
const near = (a, b, tol = 1) => Math.abs(a - b) <= tol;

export async function run(rec, { crowd }) {
  const [w0, w1] = pick(crowd, "writer", 2);
  const [m3, m4] = pick(crowd, "marketer", 2, 3);
  const field2 = pick(crowd, "field", 1, 2)[0];
  const managerP = pick(crowd, "lead", 1, 2)[0];
  const E1 = w1, E2 = m3, E3 = m4, E4 = field2, MISSED = w0;
  const day = tehranDay();
  const as = (p) => qa.login(p);
  const ev = (k = key(), extra = {}) => ({ idempotencyKey: k, evidence: null, ...extra });
  const me = async (p) => (await as(p)).get("/api/v1/attendance/me");
  const shiftOf = async (p) => {
    const uid = userId(await as(p));
    return (await (await as(managerP)).get(`/api/v1/attendance/shifts?userId=${uid}&from=${day}&to=${day}`))[0];
  };

  await qa.setClock(qa.tehran(8, 50));
  await rec.step("2.9.a", "four workers clock in at 08:50 from the office network → green", async () => {
    for (const p of [E1, E2, E3, E4]) {
      const s = (await (await as(p)).post("/api/v1/attendance/clock-in", ev())).shift;
      check(s, `${p.phone} shift opened`);
      eq(s.trust, "green", `${p.phone} trust`);
      eq(s.lateMinutes, 0, `${p.phone} not late`);
    }
  });

  // A work item for the timer: the manager hands an open card to E1.
  let item;
  await rec.step("2.9.b", "worker starts the work timer on an assigned card (inside the shift)", async () => {
    await qa.setClock(qa.tehran(11, 0));
    const mgr = await as(managerP);
    const board = await mgr.get("/api/v1/work/board");
    item = board.find((w) => !["done", "cancelled"].includes(w.status));
    check(item, "an open card exists in the demo data");
    await mgr.post(`/api/v1/work/${item.id}/assign`, { userId: userId(await as(E1)), version: item.version });
    const t = await (await as(E1)).post(`/api/v1/attendance/timer/${item.id}`, {});
    eq(t.timer?.workItemId, item.id, "timer running on the card");
    eq(await (await as(E2)).refused("POST", `/api/v1/attendance/timer/${item.id}`, {}, [404]), 404, "someone else's card → 404");
  });

  await rec.step("2.9.c", "break start stops the timer; double start → 409; timer during break → 409; end → 30 min break", async () => {
    await qa.setClock(qa.tehran(12, 0));
    const a = await as(E1);
    const s = await a.post("/api/v1/attendance/break/start", ev());
    eq(s.shift.onBreak, true, "on break");
    eq(s.timer, null, "timer stopped by the break");
    eq(await a.refused("POST", "/api/v1/attendance/break/start", ev(), [409]), 409, "break start twice");
    eq(await a.refused("POST", `/api/v1/attendance/timer/${item.id}`, {}, [409]), 409, "timer while on break");
    const live = await (await as(managerP)).get("/api/v1/attendance/live");
    eq(live.find((x) => x.userId === userId(a))?.state, "break", "live board shows break");
    await qa.setClock(qa.tehran(12, 30));
    const e = await (await as(E1)).post("/api/v1/attendance/break/end", ev());
    eq(e.shift.onBreak, false, "break ended");
    eq(e.shift.breakMinutes, 30, "break minutes");
    eq(await (await as(E1)).refused("POST", "/api/v1/attendance/break/end", ev(), [409]), 409, "break end without break");
    eq(await (await as(MISSED)).refused("POST", "/api/v1/attendance/break/start", ev(), [409]), 409, "break without a shift");
  });

  await rec.step("2.9.d", "early leave at 16:00 with a reason → earlyLeaveMinutes 60", async () => {
    await qa.setClock(qa.tehran(13, 0));
    await (await as(E1)).post(`/api/v1/attendance/timer/${item.id}`, {}); // timer runs again after the break
    await qa.setClock(qa.tehran(16, 0));
    const r = await (await as(E4)).post("/api/v1/attendance/clock-out", ev(key(), { note: "وقت دکتر" }));
    eq(r.shift, null, "no open shift after clock-out");
    const s = await shiftOf(E4);
    check(near(s.earlyLeaveMinutes, 60), `early leave minutes = 60 (got ${s.earlyLeaveMinutes})`);
    eq(s.earlyLeaveReason, "وقت دکتر", "early leave reason");
    check(s.clockOutAt, "closed");
  });

  await rec.step("2.9.e", "clock-out at 17:05 stops the running timer; worked = 8h15 − 30 min break", async () => {
    await qa.setClock(qa.tehran(17, 5));
    const before = await me(E1);
    check(before.timer?.workItemId === item.id, "timer was running before clock-out");
    const r = await (await as(E1)).post("/api/v1/attendance/clock-out", ev());
    eq(r.shift, null, "shift closed");
    eq(r.timer, null, "timer stopped by clock-out");
    check(near(r.workedMinutesToday, 465), `worked minutes today = 465 (got ${r.workedMinutesToday})`);
    const s = await shiftOf(E1);
    eq(s.earlyLeaveMinutes, 0, "no early leave");
    eq(s.needsReview, false, "no review");
    const inv = (await qa.invariants()).find((i) => i.name === "one-open-timer-per-user");
    check(inv.ok, "one-open-timer invariant");
  });

  await rec.step("2.9.f", "clock-out replay with the same key is a no-op; a new clock-out without a shift → 409", async () => {
    await qa.setClock(qa.tehran(17, 10));
    const a = await as(E2);
    const k = key();
    await a.post("/api/v1/attendance/clock-out", ev(k));
    const replay = await a.post("/api/v1/attendance/clock-out", ev(k));
    eq(replay.shift, null, "still out after replay");
    eq(await a.refused("POST", "/api/v1/attendance/clock-out", ev(), [409]), 409, "clock-out with no open shift");
    const s = await shiftOf(E2);
    check(s.clockOutAt && near((new Date(s.clockOutAt) - new Date(qa.tehran(17, 10))) / 60000, 0), `clock-out time kept from the first call (${s.clockOutAt})`);
  });

  await rec.step("2.9.g", "forgotten clock-out: still open at 20:00 (end + 4 h not reached); closed at 21:30 at the scheduled end, needs review", async () => {
    await qa.setClock(qa.tehran(20, 0));
    await qa.job("forgotten-shifts");
    check((await me(E3)).shift, "E3 still open at 20:00");
    await qa.setClock(qa.tehran(21, 30));
    const n = await qa.job("forgotten-shifts");
    check(n >= 1, `job closed at least one shift (${n})`);
    const s = await shiftOf(E3);
    check(s.clockOutAt, "closed");
    check(near((new Date(s.clockOutAt) - new Date(qa.tehran(17, 0))) / 60000, 0), `closed at the scheduled end 17:00 (${s.clockOutAt})`);
    eq(s.needsReview, true, "needs review");
    check((s.reviewReason ?? "").length > 0, "has a review reason");
    eq((await me(E3)).shift, null, "worker no longer has an open shift");
  });

  await rec.step("2.9.h", "forgotten clock-out warns the worker and the admin (PROMPT.md §6.6 «هشدار»)", async () => {
    const w = await (await as(E3)).get("/api/v1/notifications");
    const kinds = w.items.map((n) => n.kind);
    check(kinds.some((k) => /attendance|shift|forgot|clock/i.test(k)), `worker got a forgotten-clock-out notification (kinds: ${kinds.join(",") || "none"})`);
    const m = await (await as(managerP)).get("/api/v1/notifications");
    const name = E3.name.replace(" (QA)", "");
    check(m.items.some((n) => `${n.title} ${n.body ?? ""}`.includes(name) && !n.kind.startsWith("absence") && !n.kind.startsWith("leave")),
      "manager got a forgotten-clock-out notification naming the worker");
  });

  await rec.step("2.9.i", "invariant hours-match-events holds after the automatic close", async () => {
    const inv = (await qa.invariants()).find((i) => i.name === "hours-match-events");
    check(inv.ok, `hours-match-events: ${JSON.stringify(inv)}`);
  });

  let corr;
  await rec.step("2.9.j", "correction request validation: nothing given, out before in, future time → refused", async () => {
    const a = await as(E3);
    check(INVALID.includes(await a.refused("POST", "/api/v1/attendance/corrections", { day, reason: "خالی" }, INVALID)), "no times");
    check(INVALID.includes(await a.refused("POST", "/api/v1/attendance/corrections",
      { day, clockIn: qa.tehran(18, 0), clockOut: qa.tehran(17, 0), reason: "برعکس" }, INVALID)), "out before in");
    check(INVALID.includes(await a.refused("POST", "/api/v1/attendance/corrections",
      { day, clockOut: qa.tehran(23, 30), reason: "آینده" }, INVALID)), "future");
    check(INVALID.includes(await a.refused("POST", "/api/v1/attendance/corrections",
      { day, clockOut: qa.tehran(18, 30), reason: "x" }, INVALID)), "short reason");
  });

  await rec.step("2.9.k", "worker asks to correct the forgotten clock-out to 18:30 → pending; a worker can't decide (403)", async () => {
    corr = await (await as(E3)).post("/api/v1/attendance/corrections", { day, clockOut: qa.tehran(18, 30), reason: "یادم رفت خروج بزنم؛ ۱۸:۳۰ رفتم" });
    eq(corr.status, "pending", "status");
    const mine = await (await as(E3)).get("/api/v1/attendance/corrections");
    check(mine.some((c) => c.id === corr.id) && mine.every((c) => c.userId === corr.userId), "worker sees only own corrections");
    const pending = await (await as(managerP)).get("/api/v1/attendance/corrections?pending=true");
    check(pending.some((c) => c.id === corr.id), "manager sees it pending");
    eq(await (await as(E1)).refused("POST", `/api/v1/attendance/corrections/${corr.id}/decide`, { approve: true }, [403]), 403, "worker decides");
    eq(await (await as(E3)).refused("POST", `/api/v1/attendance/corrections/${corr.id}/decide`, { approve: true }, [403]), 403, "requester decides own");
  });

  await rec.step("2.9.l", "manager approves → shift ends 18:30, review cleared, appended as a Correction event; deciding twice → 409", async () => {
    const mgr = await as(managerP);
    const r = await mgr.post(`/api/v1/attendance/corrections/${corr.id}/decide`, { approve: true, note: "تأیید" });
    eq(r.status, "approved", "status");
    const s = await shiftOf(E3);
    check(near((new Date(s.clockOutAt) - new Date(qa.tehran(18, 30))) / 60000, 0), `clock-out corrected to 18:30 (${s.clockOutAt})`);
    eq(s.needsReview, false, "review cleared");
    check(near(s.workedMinutes, 580), `worked 08:50–18:30 = 580 min (got ${s.workedMinutes})`);
    eq(await mgr.refused("POST", `/api/v1/attendance/corrections/${corr.id}/decide`, { approve: false }, [409]), 409, "decide twice");
  });

  await rec.step("2.9.m", "a rejected correction leaves the shift unchanged", async () => {
    const c = await (await as(E4)).post("/api/v1/attendance/corrections", { day, clockOut: qa.tehran(17, 0), reason: "تا ۱۷ بودم" });
    const r = await (await as(managerP)).post(`/api/v1/attendance/corrections/${c.id}/decide`, { approve: false, note: "ساعت ۱۶ رفتی" });
    eq(r.status, "rejected", "status");
    eq(r.decisionNote, "ساعت ۱۶ رفتی", "note");
    const s = await shiftOf(E4);
    check(near((new Date(s.clockOutAt) - new Date(qa.tehran(16, 0))) / 60000, 0), `clock-out still 16:00 (${s.clockOutAt})`);
  });

  await rec.step("2.9.n", "correction for a whole missed day (yesterday 09:00–17:00) creates the shift on approval", async () => {
    const yday = tehranDay(-1);
    const c = await (await as(MISSED)).post("/api/v1/attendance/corrections",
      { day: yday, clockIn: qa.tehran(9, 0, -1), clockOut: qa.tehran(17, 0, -1), reason: "گوشی‌ام خاموش بود، کل روز دفتر بودم" });
    await (await as(managerP)).post(`/api/v1/attendance/corrections/${c.id}/decide`, { approve: true });
    const uid = userId(await as(MISSED));
    const shifts = await (await as(managerP)).get(`/api/v1/attendance/shifts?userId=${uid}&from=${yday}&to=${yday}`);
    eq(shifts.length, 1, "one shift yesterday");
    eq(shifts[0].workedMinutes, 480, "worked minutes");
    check(shifts[0].clockOutAt, "closed");
  });

  await rec.step("2.9.q", "a clock-in-only correction for a past day while the worker is clocked in must not leave two open shifts", async () => {
    // MISSED works late tonight (open shift), then asks: "on Saturday I came at 09:00 but the app failed" (no clock-out given).
    const a = await as(MISSED);
    await a.post("/api/v1/attendance/clock-in", ev());
    const d2 = tehranDay(-2);
    const c = await a.post("/api/v1/attendance/corrections", { day: d2, clockIn: qa.tehran(9, 0, -2), reason: "شنبه ساعت ۹ آمدم، برنامه خطا داد" });
    await (await as(managerP)).call("POST", `/api/v1/attendance/corrections/${c.id}/decide`, { approve: true }, { expect: [200, 400, 409, 422] });
    const inv = (await qa.invariants()).find((i) => i.name === "one-open-shift-per-user");
    const uid = userId(a);
    const open = (await (await as(managerP)).get(`/api/v1/attendance/shifts?userId=${uid}&from=${d2}&to=${day}`)).filter((s) => !s.clockOutAt);
    await (await as(MISSED)).call("POST", "/api/v1/attendance/clock-out", ev(), { expect: [200, 409] });
    check(open.length <= 1, `open shifts for the worker: ${open.length} (${open.map((s) => s.day).join(",")})`);
    check(inv.ok, `one-open-shift-per-user: ${JSON.stringify(inv)}`);
  });

  await rec.step("2.9.o", "invariants after the evening (hours-match-events, one open shift/timer)", async () => {
    const inv = await qa.invariants();
    for (const name of ["hours-match-events", "one-open-shift-per-user", "one-open-timer-per-user"]) {
      const i = inv.find((x) => x.name === name);
      check(i?.ok, `${name}: ${JSON.stringify(i)}`);
    }
  });

  await rec.step("2.9.p", "live board at night: the evening crew shows 'out'", async () => {
    const live = await (await as(managerP)).get("/api/v1/attendance/live");
    for (const p of [E1, E2, E3, E4]) {
      const uid = userId(await as(p));
      eq(live.find((x) => x.userId === uid)?.state, "out", `${p.phone} state`);
    }
  });
}
