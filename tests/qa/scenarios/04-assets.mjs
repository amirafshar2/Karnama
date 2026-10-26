// S04 — equipment (docs/PROMPT3.md §2.4, PROMPT2.md §3): camera hand-over with a photo → the worker accepts;
// direct transfer between two workers; late return (QA clock past ExpectedReturn + "asset-alerts") → one alert;
// return with a damage report → the manager confirms. Error paths: hand over an asset that is out (409), someone else
// accepting a hand-over (403/404), cancelling a pending hand-over, a worker trying manager-only actions.
import { randomInt } from "node:crypto";
import { qa, pick, check, eq } from "../lib.mjs";

export const id = "S04";
export const title = "تجهیزات — تحویل با عکس، تأیید کارگر، انتقال مستقیم، دیرکرد، برگشت با خرابی";

const userId = (actor) => JSON.parse(Buffer.from(actor.token.split(".")[1], "base64url").toString()).sub;
const ANY = Array.from({ length: 500 }, (_, i) => i + 100);
const CAMERA = "0193a100-0000-7000-8000-000000000002";
const TRIPOD = "0193a100-0000-7000-8000-000000000006";
// 1×1 PNG.
const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");
const tehranDate = (offsetDays) => {
  const t = new Date(Date.now() + 3.5 * 3600_000 + offsetDays * 86_400_000);
  return t.toISOString().slice(0, 10);
};
const photoForm = (name = "camera.png", bytes = PNG, type = "image/png") => {
  const f = new FormData();
  f.append("file", new Blob([bytes], { type }), name);
  return f;
};

export async function run(rec, { crowd }) {
  const [managerP] = pick(crowd, "lead", 1, 2);
  const [w1P, w2P, w3P] = pick(crowd, "field", 3, 1);
  const run0 = randomInt(100000, 999999);
  await qa.setClock(qa.tehran(9, 0));
  let manager = await qa.login(managerP);
  let w1 = await qa.login(w1P, { android: true });
  let w2 = await qa.login(w2P, { android: true });
  let w3 = await qa.login(w3P);
  const relogin = async () => { manager = await qa.login(managerP); w1 = await qa.login(w1P); w2 = await qa.login(w2P); w3 = await qa.login(w3P); };
  const ids = { w1: userId(w1), w2: userId(w2), w3: userId(w3), manager: userId(manager) };

  let camera, tripod, handover;
  await rec.step("2.4.a", "manager registers a camera and a tripod (tag = property code, unique)", async () => {
    camera = (await manager.post("/api/v1/assets", { name: `دوربین QA ${run0}`, categoryId: CAMERA, brand: "Canon", model: "R6", serial: `SN-C-${run0}`, tag: `QC-${run0}`,
      purchasedOn: "2025-01-10", purchasePrice: 900_000_000, warrantyUntil: "2027-01-10", note: "سناریو S04" })).asset;
    eq(camera.status, "available", "new asset available");
    tripod = (await manager.post("/api/v1/assets", { name: `سه‌پایه QA ${run0}`, categoryId: TRIPOD, tag: `QT-${run0}` })).asset;
    const dup = await manager.call("POST", "/api/v1/assets", { name: "تکراری", categoryId: CAMERA, tag: `QC-${run0}` }, { expect: ANY });
    eq(dup.status, 409, `duplicate tag (${dup.code})`);
    eq(await w1.refused("POST", "/api/v1/assets", { name: "x", categoryId: CAMERA }, [403]), 403, "worker cannot create assets");
  });

  await rec.step("2.4.b", "camera handed to worker 1 with condition, accessories, expected return → pending acceptance; worker notified", async () => {
    const d = await manager.post(`/api/v1/assets/${camera.id}/handover`, {
      userId: ids.w1, expectedReturn: tehranDate(1), condition: "سالم، بدون خط و خش", accessories: ["شارژر", "کیف", "باتری ×۲"], version: camera.version,
    });
    handover = d.current;
    eq(d.asset.status, "assigned", "asset status");
    eq(d.asset.holderId, ids.w1, "holder");
    eq(handover.state, "pendingAcceptance", "waiting for the worker");
    eq(handover.accessories.length, 3, "accessories kept");
    check(handover.handedAt, "hand-over time recorded");
    check((await w1.get("/api/v1/notifications")).items.some((n) => n.kind === "assets.handover"), "worker got assets.handover");
  });

  await rec.step("2.4.c", "hand-over photo (multipart) is attached to the hand-over; a non-image is refused", async () => {
    const p = await manager.post(`/api/v1/assets/${camera.id}/photos?stage=HandOver&assignmentId=${handover.id}`, undefined, { form: photoForm() });
    eq(p.stage, "handOver", "stage");
    eq(p.assignmentId, handover.id, "linked to the hand-over");
    const bad = await manager.call("POST", `/api/v1/assets/${camera.id}/photos?stage=HandOver&assignmentId=${handover.id}`, undefined,
      { form: photoForm("x.png", Buffer.from("<script>alert(1)</script>"), "image/png"), expect: ANY });
    eq(bad.status, 422, `fake image refused (${bad.code})`);
    const noAssignment = await manager.call("POST", `/api/v1/assets/${camera.id}/photos?stage=HandOver`, undefined, { form: photoForm(), expect: ANY });
    eq(noAssignment.status, 422, "condition photo must belong to a hand-over");
    // The holder can see the photo of their own hand-over.
    const d = await w1.get(`/api/v1/assets/${camera.id}`);
    check(d.photos.some((x) => x.id === p.id), "worker sees the hand-over photo");
  });

  await rec.step("2.4.d", "camera already out → hand over again is 409 (assets.held); version is fresh so it is the business rule", async () => {
    const fresh = (await manager.get(`/api/v1/assets/${camera.id}`)).asset;
    const r = await manager.call("POST", `/api/v1/assets/${camera.id}/handover`, { userId: ids.w2, version: fresh.version }, { expect: ANY });
    eq(r.status, 409, "status");
    eq(r.code, "assets.held", "code");
  });

  await rec.step("2.4.e", "someone else cannot accept worker 1's hand-over (404 for an outsider, nothing changes)", async () => {
    const r = await w2.call("POST", `/api/v1/assets/assignments/${handover.id}/accept`, undefined, { expect: ANY });
    check([403, 404].includes(r.status), `outsider accept refused (${r.status})`);
    const other = await w2.call("GET", `/api/v1/assets/${camera.id}`, undefined, { expect: ANY });
    eq(other.status, 404, "outsider cannot even open the asset");
    // Even the manager cannot accept on the worker's behalf (digital acceptance is personal).
    const m = await manager.call("POST", `/api/v1/assets/assignments/${handover.id}/accept`, undefined, { expect: ANY });
    eq(m.status, 403, "manager cannot accept for the worker");
    eq((await manager.get(`/api/v1/assets/${camera.id}`)).current.state, "pendingAcceptance", "still pending");
  });

  await rec.step("2.4.f", "worker 1 accepts → active with acceptance time; manager notified; listed under «پیش من»", async () => {
    const d = await w1.post(`/api/v1/assets/assignments/${handover.id}/accept`);
    eq(d.current.state, "active", "state");
    check(d.current.acceptedAt, "acceptedAt");
    check((await manager.get("/api/v1/notifications")).items.some((n) => n.kind === "assets.accepted"), "manager got assets.accepted");
    const mine = await w1.get("/api/v1/assets/mine");
    check(mine.holding.some((a) => a.id === camera.id), "in worker 1's list");
    const again = await w1.call("POST", `/api/v1/assets/assignments/${handover.id}/accept`, undefined, { expect: ANY });
    eq(again.status, 409, "double accept → 409");
  });

  await rec.step("2.4.g", "cancel: a pending tripod hand-over to worker 3 is cancelled by the manager → back on the shelf; worker cannot cancel; accepted ones cannot be cancelled", async () => {
    const d = await manager.post(`/api/v1/assets/${tripod.id}/handover`, { userId: ids.w3, version: tripod.version });
    const x = d.current;
    eq(await w3.refused("POST", `/api/v1/assets/assignments/${x.id}/cancel`, undefined, [403]), 403, "worker cannot cancel");
    const c = await manager.post(`/api/v1/assets/assignments/${x.id}/cancel`);
    eq(c.asset.status, "available", "available again");
    eq(c.asset.holderId, null, "no holder");
    check(c.history.some((e) => e.kind === "cancelled"), "history has the cancel");
    const late = await w3.call("POST", `/api/v1/assets/assignments/${x.id}/accept`, undefined, { expect: ANY });
    check([404, 409].includes(late.status), `accepting a cancelled hand-over refused (${late.status})`);
    const cam = await manager.call("POST", `/api/v1/assets/assignments/${handover.id}/cancel`, undefined, { expect: ANY });
    eq(cam.status, 409, "cannot cancel an accepted hand-over");
  });

  await rec.step("2.4.g", "decline: worker 3 refuses a tripod hand-over with a note → available again, manager gets an important notice", async () => {
    const t = (await manager.get(`/api/v1/assets/${tripod.id}`)).asset;
    const x = (await manager.post(`/api/v1/assets/${tripod.id}/handover`, { userId: ids.w3, version: t.version })).current;
    const d = await w3.post(`/api/v1/assets/assignments/${x.id}/decline`, { note: "سه‌پایه پایه‌اش شکسته بود" });
    eq(d.asset.status, "available", "available");
    const n = (await manager.get("/api/v1/notifications")).items.find((i) => i.kind === "assets.declined");
    check(n?.important && n.body?.includes("شکسته"), `manager told, with the note (${JSON.stringify(n)})`);
  });

  await rec.step("2.4.g", "race: two hand-overs of the tripod at the same moment → exactly one wins (409 for the other)", async () => {
    const t = (await manager.get(`/api/v1/assets/${tripod.id}`)).asset;
    const owner = await qa.login(pick(crowd, "lead", 1, 0)[0]);
    const [a, b] = await Promise.all([
      manager.call("POST", `/api/v1/assets/${tripod.id}/handover`, { userId: ids.w3, version: t.version }, { expect: ANY }),
      owner.call("POST", `/api/v1/assets/${tripod.id}/handover`, { userId: ids.w1, version: t.version }, { expect: ANY }),
    ]);
    const ok = [a, b].filter((r) => r.asset).length;
    const conflicts = [a, b].filter((r) => r.status === 409).length;
    eq(ok, 1, `one succeeded (${JSON.stringify([a.status ?? "ok", b.status ?? "ok"])})`);
    eq(conflicts, 1, "the other is a 409");
    const d = await manager.get(`/api/v1/assets/${tripod.id}`);
    const winner = d.current;
    // Clean up: take the pending hand-over back.
    await manager.post(`/api/v1/assets/assignments/${winner.id}/cancel`);
  });

  let transferred;
  await rec.step("2.4.h", "direct transfer: worker 1 offers the camera to worker 2; worker 3 cannot take it; worker 2 accepts → new active hand-over, no stop at the shelf", async () => {
    const o = await w1.post(`/api/v1/assets/assignments/${handover.id}/transfer`, { userId: ids.w2 });
    eq(o.current.transferToId, ids.w2, "offered to worker 2");
    const inc = await w2.get("/api/v1/assets/mine");
    check(inc.incoming.some((a) => a.id === camera.id), "worker 2 sees it as incoming");
    const steal = await w3.call("POST", `/api/v1/assets/assignments/${handover.id}/transfer/accept`, {}, { expect: ANY });
    check([403, 404].includes(steal.status), `worker 3 refused (${steal.status})`);
    const self = await w1.call("POST", `/api/v1/assets/assignments/${handover.id}/transfer/accept`, {}, { expect: ANY });
    eq(self.status, 403, "the giver cannot accept for the receiver");
    const a = await w2.post(`/api/v1/assets/assignments/${handover.id}/transfer/accept`, { condition: "تحویل گرفتم، سالم" });
    transferred = a.current;
    eq(a.asset.holderId, ids.w2, "holder is worker 2");
    eq(a.asset.status, "assigned", "never went back to the shelf");
    eq(transferred.state, "active", "already accepted");
    eq(transferred.expectedReturn, tehranDate(1), "expected return carried over");
    check(a.history.some((e) => e.kind === "transferred"), "history has the transfer");
    check((await w1.get("/api/v1/notifications")).items.some((n) => n.kind === "assets.transferred"), "worker 1 told the transfer happened");
    check(!(await w1.get("/api/v1/assets/mine")).holding.some((x) => x.id === camera.id), "gone from worker 1's list");
  });

  await rec.step("2.4.h", "transfer offer can be withdrawn (cancel) before it is accepted", async () => {
    const o = await w2.post(`/api/v1/assets/assignments/${transferred.id}/transfer`, { userId: ids.w3 });
    eq(o.current.transferToId, ids.w3, "offered");
    const c = await w2.post(`/api/v1/assets/assignments/${transferred.id}/transfer/cancel`);
    eq(c.current.transferToId, null, "offer withdrawn");
    eq((await w3.call("GET", `/api/v1/assets/${camera.id}`, undefined, { expect: ANY })).status, 404, "worker 3 no longer sees it");
  });

  await rec.step("2.4.i", "late return: QA clock 3 days ahead + asset-alerts → overdue flag and exactly one alert to the holder (re-run same day: none)", async () => {
    await qa.advance(3 * 24 * 60);
    await relogin();
    const before = (await w2.get("/api/v1/notifications")).items.filter((n) => n.kind === "assets.overdue").length;
    await qa.job("asset-alerts");
    const item = (await manager.get(`/api/v1/assets?q=${encodeURIComponent(`QC-${run0}`)}`))[0];
    eq(item.overdue, true, "overdue flag on the list");
    const after = (await w2.get("/api/v1/notifications")).items.filter((n) => n.kind === "assets.overdue");
    eq(after.length - before, 1, "one alert to the holder");
    check(after.some((n) => n.title.includes(camera.name)), "names the asset");
    check((await manager.get("/api/v1/notifications")).items.some((n) => n.kind === "assets.overdue" && n.body?.includes(w2P.name)), "managers told who has it");
    await qa.job("asset-alerts");
    eq((await w2.get("/api/v1/notifications")).items.filter((n) => n.kind === "assets.overdue").length, after.length, "same day: no second alert");
  });

  await rec.step("2.4.j", "worker 2 returns with a damage report + return photo → waiting for the manager (important notice)", async () => {
    const r = await w2.post(`/api/v1/assets/assignments/${transferred.id}/return`, { condition: "لنز کثیف", damage: "ترک روی LCD" });
    eq(r.current.state, "returnSubmitted", "state");
    eq(r.current.damage, "ترک روی LCD", "damage kept");
    const p = await w2.post(`/api/v1/assets/${camera.id}/photos?stage=Return&assignmentId=${transferred.id}`, undefined, { form: photoForm("return.png") });
    eq(p.stage, "return", "return photo");
    const n = (await manager.get("/api/v1/notifications")).items.find((x) => x.kind === "assets.return");
    check(n?.important && n.body.includes("ترک"), `manager got an important return notice with the damage (${JSON.stringify(n)})`);
    const again = await w2.call("POST", `/api/v1/assets/assignments/${transferred.id}/return`, { condition: "x" }, { expect: ANY });
    eq(again.status, 409, "double return → 409");
    eq(await w2.refused("POST", `/api/v1/assets/assignments/${transferred.id}/return/confirm`, { status: "available" }, [403]), 403, "worker cannot confirm own return");
  });

  await rec.step("2.4.k", "manager confirms the return as InRepair → asset off duty, no holder, full append-only history", async () => {
    const bad = await manager.call("POST", `/api/v1/assets/assignments/${transferred.id}/return/confirm`, { status: "assigned" }, { expect: ANY });
    eq(bad.status, 422, "invalid next status");
    const d = await manager.post(`/api/v1/assets/assignments/${transferred.id}/return/confirm`, { status: "inRepair", damage: "ترک روی LCD — ارسال به تعمیر" });
    eq(d.asset.status, "inRepair", "in repair");
    eq(d.asset.holderId, null, "no holder");
    eq(d.current, null, "no open hand-over");
    const kinds = d.history.map((e) => e.kind);
    for (const k of ["created", "handedOver", "accepted", "transferOffered", "transferred", "returnSubmitted", "returned"]) check(kinds.includes(k), `history has ${k} (${kinds.join(",")})`);
    check(d.past.some((p) => p.id === transferred.id && p.state === "returned"), "past hand-overs listed");
    const h = await manager.call("POST", `/api/v1/assets/${camera.id}/handover`, { userId: ids.w1, version: d.asset.version }, { expect: ANY });
    eq(h.status, 409, "an asset in repair cannot be handed out");
    check((await w2.get("/api/v1/notifications")).items.some((n) => n.kind === "assets.returned"), "worker 2 told the return is closed");
  });

  await rec.step("2.4.l", "invariant: at most one live hand-over per asset", async () => {
    const inv = (await qa.invariants()).find((i) => i.name === "one-active-handover-per-asset");
    check(inv?.ok, JSON.stringify(inv));
  });
}
