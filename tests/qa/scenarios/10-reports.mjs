// S10 — Reports & scores (docs/PROMPT3.md §2.10, PROMPT2 §9 + §11): period report per worker and team, Excel export
// (a real XLSX with rows), scores with "why this score", and the fairness rule: work that is late because the CLIENT did
// not answer in time must not lower the worker's on-time score.
import { inflateRawSync } from "node:zlib";
import { qa, pick, key, check, eq, API } from "../lib.mjs";

export const id = "S10";
export const title = "گزارش بازه‌ای، خروجی Excel، امتیازها و «چرا این امتیاز»، تأخیر مشتری بی‌اثر";

// People (kept apart from S02): lead[0] = owner (reports.view/export, scores.view all)
//   office[11] = X (photographer: two single posts) · office[13] = Y (influencer stage 1) · office[14] = Z (influencer stage 2)
//   client[6] = X's client (agency-managed) · client[7] = influencer client
const H = 60;
const signedIn = [];
async function login(person, opts) { const a = await qa.login(person, opts); signedIn.push(a); return a; }
async function advance(minutes) {
  await qa.advance(minutes);
  for (const a of signedIn) a.token = (await qa.login(a.person)).token;   // access tokens live 15 minutes on the QA clock
}
async function cardOf(actor, workId) { return (await actor.get(`/api/v1/work/${workId}`)).card; }
async function move(actor, workId, status, note) {
  const c = await cardOf(actor, workId);
  return actor.post(`/api/v1/work/${workId}/move`, { status, version: c.version, note });
}
async function assign(manager, workId, userId) {
  const c = await cardOf(manager, workId);
  return manager.post(`/api/v1/work/${workId}/assign`, { userId, version: c.version });
}
/** Tehran calendar date (yyyy-mm-dd) of an ISO instant. */
const tehranDay = (iso) => new Date(Date.parse(iso) + 3.5 * 3600_000).toISOString().slice(0, 10);

/** Minimal ZIP reader (central directory): returns { name → Buffer }. Enough to look inside an .xlsx without dependencies. */
function unzip(buf) {
  let eocd = -1;
  for (let i = buf.length - 22; i >= Math.max(0, buf.length - 65_557); i--) if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  if (eocd < 0) throw new Error("not a zip: no end-of-central-directory record");
  const count = buf.readUInt16LE(eocd + 10);
  let p = buf.readUInt32LE(eocd + 16);
  const out = {};
  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(p) !== 0x02014b50) throw new Error("bad central directory entry");
    const method = buf.readUInt16LE(p + 10), csize = buf.readUInt32LE(p + 20);
    const nlen = buf.readUInt16LE(p + 28), xlen = buf.readUInt16LE(p + 30), clen = buf.readUInt16LE(p + 32), local = buf.readUInt32LE(p + 42);
    const name = buf.subarray(p + 46, p + 46 + nlen).toString("utf8");
    const start = local + 30 + buf.readUInt16LE(local + 26) + buf.readUInt16LE(local + 28);
    const data = buf.subarray(start, start + csize);
    out[name] = method === 0 ? Buffer.from(data) : method === 8 ? inflateRawSync(data) : null;
    p += 46 + nlen + xlen + clen;
  }
  return out;
}

export async function run(rec, { crowd }) {
  signedIn.length = 0;
  // Day +2 so this scenario's work does not share report days with S02 (day 0).
  const t0 = qa.tehran(9, 0, 2);
  await qa.setClock(t0);
  const day0 = tehranDay(t0);
  const day1 = tehranDay(new Date(Date.parse(t0) + 5 * 86400_000).toISOString());   // the story spans ~4.2 days
  const owner = await login(pick(crowd, "lead", 1, 0)[0]);
  const [xP, yP, zP] = [pick(crowd, "office", 1, 11)[0], pick(crowd, "office", 1, 13)[0], pick(crowd, "office", 1, 14)[0]];
  const X = await login(xP), Y = await login(yP), Z = await login(zP);
  const c6 = await login(pick(crowd, "client", 1, 6)[0]);
  const c7 = await login(pick(crowd, "client", 1, 7)[0]);
  const s = {};

  await rec.step("2.10.a", "داده‌ی طراحی‌شده: دو پست تکی برای X، کمپین دومرحله‌ای برای Y و Z", async () => {
    for (const [a, k] of [[X, "X"], [Y, "Y"], [Z, "Z"]]) s[k] = (await a.get("/api/v1/me")).id;
    const services = Object.fromEntries((await owner.get("/api/v1/services/options")).map((o) => [o.name, o.id]));
    const clientOf = async (c) => (await owner.get("/api/v1/clients?q=" + encodeURIComponent((await c.get("/api/v1/portal/home")).clientName))).items[0].id;
    s.c6 = await clientOf(c6); s.c7 = await clientOf(c7);
    const order = (clientId, svc, t) => owner.post("/api/v1/orders", { clientId, serviceTypeId: services[svc], title: t, priority: "normal", autoAccept: true, answers: { topic: t } });
    s.o1 = await order(s.c6, "پست تکی", "S10 X1 — مشتری دیر تأیید می‌کند");
    s.o2 = await order(s.c6, "پست تکی", "S10 X2 — کارگر واقعاً دیر تحویل می‌دهد");
    s.o3 = await order(s.c7, "کمپین اینفلوئنسر", "S10 کمپین — مرحله‌ی ۱ منتظر مشتری");
    s.x1 = s.o1.stages[0].id; s.x2 = s.o2.stages[0].id;
    [s.y1, s.z2] = s.o3.stages.sort((a, b) => a.order - b.order).map((x) => x.id);
    eq(s.o3.stages.length, 2, "influencer campaign has two stages");
    s.deadline = { x1: s.o1.stages[0].deadline, y1: s.o3.stages[0].deadline, z2: s.o3.stages[1].deadline };
    const h = (Date.parse(s.deadline.z2) - Date.parse(t0)) / 3600_000;
    check(Math.abs(h - 96) < 0.1, `stage 2 deadline fixed at accept time: 48h + 48h (got ${h.toFixed(2)}h)`);
    await assign(owner, s.x1, s.X); await assign(owner, s.x2, s.X); await assign(owner, s.y1, s.Y); await assign(owner, s.z2, s.Z);
  });

  await rec.step("2.10.b", "روز ۱: X کار ۱ را با تایمر انجام و زود تحویل می‌دهد؛ Y مرحله‌ی ۱ را زود تحویل می‌دهد", async () => {
    await X.post("/api/v1/attendance/clock-in", { idempotencyKey: key() });
    await move(X, s.x1, "inProgress");
    await X.post(`/api/v1/attendance/timer/${s.x1}`);
    await advance(30);
    await X.del("/api/v1/attendance/timer");
    await move(X, s.x1, "inReview");
    await move(owner, s.x1, "clientReview");
    await move(Y, s.y1, "inProgress");
    await advance(10);
    await move(Y, s.y1, "inReview");
    await move(owner, s.y1, "clientReview");
    await X.post("/api/v1/attendance/clock-out", { idempotencyKey: key() });
    eq((await cardOf(owner, s.x1)).status, "clientReview", "X1 waits for the client");
    eq((await cardOf(owner, s.z2)).status, "waiting", "stage 2 waits for stage 1");
  });

  await rec.step("2.10.c", "مشتری ۳ روز جواب نمی‌دهد (بعد از ددلاین)؛ X کار ۲ را خودش دیر تحویل می‌دهد", async () => {
    await advance(72 * H - 40);                        // day 3, 09:00 — X1/Y1 deadlines (t0+48h) have passed
    check(Date.parse((await qa.clock()).now) > Date.parse(s.deadline.x1), "now past X1's deadline");
    await c6.post(`/api/v1/portal/approvals/${s.x1}`, { approve: true, stars: 5 });
    eq((await cardOf(owner, s.x1)).status, "done", "client finally approves X1 (late, client's fault)");
    // X2: the worker starts only now, after its deadline → genuinely late
    await X.post("/api/v1/attendance/clock-in", { idempotencyKey: key() });
    await move(X, s.x2, "inProgress");
    await X.post(`/api/v1/attendance/timer/${s.x2}`);
    await advance(60);
    await X.del("/api/v1/attendance/timer");
    await move(X, s.x2, "inReview");
    await move(owner, s.x2, "clientReview");
    await c6.post(`/api/v1/portal/approvals/${s.x2}`, { approve: true });
    await X.post("/api/v1/attendance/clock-out", { idempotencyKey: key() });
    eq((await cardOf(owner, s.x2)).status, "done", "X2 done");
  });

  await rec.step("2.10.d", "مشتری کمپین بعد از ۴ روز مرحله‌ی ۱ را تأیید می‌کند → Z بلافاصله مرحله‌ی ۲ را تحویل می‌دهد", async () => {
    await advance(28 * H);                             // t0 + 100h: past stage 2's deadline (t0 + 96h)
    await c7.post(`/api/v1/portal/approvals/${s.y1}`, { approve: true });
    eq((await cardOf(owner, s.z2)).status, "todo", "stage 2 unlocked only now — its deadline already passed while the client was silent");
    await move(Z, s.z2, "inProgress");
    await advance(30);
    await move(Z, s.z2, "inReview");                  // Z needs 30 minutes: as fast as anyone could
    await move(owner, s.z2, "done");
    eq((await owner.get(`/api/v1/orders/${s.o3.id}`)).status, "completed", "campaign completed");
    check(tehranDay((await qa.clock()).now) <= day1, "story stays inside the report range");
  });

  const range = () => `from=${day0}&to=${day1}`;

  await rec.step("2.10.e", "گزارش بازه‌ای کل تیم و هر کارگر (ردیف‌ها، نمودار روزانه، جزئیات)", async () => {
    const team = await owner.get(`/api/v1/reports/work?${range()}`);
    eq(team.days.length, Date.parse(day1) / 86400_000 - Date.parse(day0) / 86400_000 + 1, "one chart point per day of the range");
    const row = (id) => team.rows.find((r) => r.userId === id);
    for (const k of ["X", "Y", "Z"]) check(row(s[k]), `team report has a row for ${k}`);
    check(team.rows.length >= 20, `whole team listed (${team.rows.length} rows)`);
    const x = row(s.X);
    eq(x.doneCount, 2, "X finished 2 items in the range");
    eq(x.timerMinutes, 90, "X: 30 + 60 timer minutes");
    eq(x.actualMinutes, 90, "actual time on finished items");
    eq(x.estimatedMinutes, 180, "estimate 2 × 90");
    check(x.presenceMinutes >= 90, "presence ≥ timer time");
    check(x.usefulPercent > 0 && x.usefulPercent <= 100, "useful-work ratio in 0–100");
    eq(x.doneByService[0]?.count, 2, "done by service type");
    eq(team.days.reduce((n, d) => n + d.done, 0) >= 4, true, "daily chart counts the finished items");
    const one = await owner.get(`/api/v1/reports/work?${range()}&user=${s.X}`);
    eq(one.rows.length, 1, "?user= narrows to one worker");
    const det = await owner.get(`/api/v1/reports/work/${s.X}?${range()}`);
    eq(det.shifts.length, 2, "X: two shifts in the details");
    eq(det.timers.length, 2, "X: two timer runs in the details");
    eq(det.done.length, 2, "X: two finished items in the details");
    // range validation
    eq(await owner.refused("GET", `/api/v1/reports/work?from=${day1}&to=${day0}`, undefined, [422]), 422, "to < from → 422");
    eq(await owner.refused("GET", `/api/v1/reports/work?from=2025-01-01&to=2025-12-31`, undefined, [422]), 422, "range > 92 days → 422");
  });

  await rec.step("2.10.f", "کارگر فقط گزارش خودش را می‌بیند؛ گزارش دیگری → 404؛ خروجی Excel → 403", async () => {
    const mine = await X.get(`/api/v1/reports/work?${range()}`);
    eq(mine.rows.length, 1, "worker sees one row");
    eq(mine.rows[0].userId, s.X, "…their own");
    eq(await X.refused("GET", `/api/v1/reports/work/${s.Y}?${range()}`, undefined, [404]), 404, "someone else's details");
    eq(await X.refused("GET", `/api/v1/reports/work/export?${range()}`, undefined, [403]), 403, "export needs reports.export");
    eq(await c6.refused("GET", `/api/v1/reports/work?${range()}`, undefined, [403]), 403, "a client gets no reports");
  });

  await rec.step("2.10.g", "خروجی Excel: فایل واقعی XLSX (PK، نوع، نام فایل) با ردیف کارگرها", async () => {
    const res = await fetch(`${API}/api/v1/reports/work/export?${range()}`, { headers: { Authorization: `Bearer ${owner.token}` } });
    eq(res.status, 200, "export ok");
    eq(res.headers.get("content-type"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "XLSX content type");
    check(/attachment/.test(res.headers.get("content-disposition") ?? "") && /\.xlsx/.test(res.headers.get("content-disposition") ?? ""), `download with .xlsx name (${res.headers.get("content-disposition")})`);
    const buf = Buffer.from(await res.arrayBuffer());
    eq(buf.subarray(0, 4).toString("latin1"), "PK\x03\x04", "zip magic");
    const files = unzip(buf);
    check(files["[Content_Types].xml"] && files["xl/workbook.xml"], "OOXML workbook parts present");
    const xml = Object.entries(files).filter(([n]) => n.startsWith("xl/") && n.endsWith(".xml")).map(([, b]) => b?.toString("utf8") ?? "").join("\n");
    for (const p of [xP, yP, zP]) check(xml.includes(p.name), `sheet has a row for ${p.name}`);
    check(xml.includes("کار تمام‌شده") && xml.includes("امتیاز"), "Persian headers present");
    const sheet = Object.entries(files).find(([n]) => /^xl\/worksheets\/sheet\d+\.xml$/.test(n))?.[1]?.toString("utf8") ?? "";
    const rows = (sheet.match(/<(?:x:)?row\b/g) ?? []).length;
    check(rows >= 4, `sheet has a header + rows (${rows})`);
    // PDF: the web page prints itself (browser print → PDF); there is no server PDF endpoint to check here.
  });

  await rec.step("2.10.h", "امتیازها + «چرا این امتیاز» برای هر نفر؛ کمتر از ۵ نمونه → «داده‌ی کافی نیست»", async () => {
    const sc = await owner.get(`/api/v1/reports/scores?${range()}`);
    eq(sc.rankingVisible, true, "owner sees the team ranking");
    const keys = ["quality", "speed", "onTime", "volume", "discipline", "manager", "client"];
    for (const p of sc.people) {
      eq(p.indicators.map((i) => i.key).join(","), keys.join(","), `${p.name}: all 7 indicators`);
      for (const i of p.indicators) check(i.value && i.hint && i.weight >= 0, `${p.name}/${i.key} explains itself (value + hint)`);
    }
    const x = sc.people.find((p) => p.userId === s.X);
    eq(x.samples, 2, "X has 2 samples");
    eq(x.enough, false, "below the minimum of 5 → not enough data");
    eq(x.total, null, "no total score without enough data");
    eq(x.indicators.find((i) => i.key === "client").score, 100, "client's 5 stars → 100");
    eq(x.indicators.find((i) => i.key === "quality").score, 100, "both approved first time");
    // a worker sees only their own explanation (ranking for all is off by default)
    const own = await X.get(`/api/v1/reports/scores?${range()}`);
    eq(own.people.length, 1, "worker sees one person");
    eq(own.people[0].userId, s.X, "…themselves");
    eq(own.people[0].indicators.length, 7, "with the 'why' of every indicator");
    eq(await X.refused("GET", `/api/v1/reports/scores/${s.Y}/trend`, undefined, [404]), 404, "no one else's trend");
    const trend = await X.get(`/api/v1/reports/scores/${s.X}/trend`);
    eq(trend.length, 6, "six-month trend");
  });

  await rec.step("2.10.i", "تأخیر به‌خاطر مشتری (تأیید دیر) روی «به‌موقع» اثر ندارد؛ تأخیر واقعی کارگر اثر دارد", async () => {
    const det = await owner.get(`/api/v1/reports/work/${s.X}?${range()}`);
    const x1 = det.done.find((d) => d.id === s.x1), x2 = det.done.find((d) => d.id === s.x2);
    check(Date.parse(x1.delivered) < Date.parse(x1.deadline), "X1 delivered (first hand-in) before its deadline");
    eq(x1.onTime, true, "X1 counts as on time although the client approved 3 days later");
    eq(x2.onTime, false, "X2 (worker started after the deadline) counts as late — the control case");
    const y = await owner.get(`/api/v1/reports/work/${s.Y}?${range()}`);
    eq(y.done.find((d) => d.id === s.y1)?.onTime, true, "Y's stage 1 is on time although the client sat on it for 4 days");
    const sc = await owner.get(`/api/v1/reports/scores?${range()}`);
    eq(sc.people.find((p) => p.userId === s.X).indicators.find((i) => i.key === "onTime").score, 50, "X on-time = 1 of 2 (only the real delay counts)");
    eq(sc.people.find((p) => p.userId === s.Y).indicators.find((i) => i.key === "onTime").score, 100, "Y on-time = 100");
  });

  await rec.step("2.10.j", "مرحله‌ی بعدی که به‌خاطر سکوت مشتری دیر باز شد نباید امتیاز Z را کم کند", async () => {
    const z = await owner.get(`/api/v1/reports/work/${s.Z}?${range()}`);
    const line = z.done.find((d) => d.id === s.z2);
    check(line, "Z's stage 2 is in the report");
    const sc = await owner.get(`/api/v1/reports/scores?${range()}`);
    const onTime = sc.people.find((p) => p.userId === s.Z).indicators.find((i) => i.key === "onTime");
    // Z had the item for 30 minutes; the stage deadline (accept + 96h) expired while stage 1 waited for the client.
    check(line.onTime !== false, `Z's stage 2 must not count as late (got onTime=${line.onTime}, deadline ${line.deadline}, delivered ${line.delivered})`);
    check(onTime.score !== 0, `Z's on-time indicator must not drop to 0 because of the client (got ${onTime.score}: ${onTime.value})`);
  });
}
