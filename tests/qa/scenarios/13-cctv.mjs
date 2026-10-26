// S13 — office cameras (docs/PROMPT3.md §2 item 13; PROMPT2.md §5).
// There is no real camera or MediaMTX box in QA. With the standard QA API the media box is "not configured";
// started with Cctv__ApiUrl/PublicUrl/InternalUser/InternalPassword pointing at a dead MediaMTX it is "configured but unreachable".
// Either way the API must degrade with a clear 409 (never a 500), keep camera passwords secret, and refuse people without cctv.view.
// A real snapshot → live → idle-stop loop needs MediaMTX with an RTSP loop source on the server (see findings D.md).
import { qa, check, eq, pick, DEMO } from "../lib.mjs";

export const id = "S13";
export const title = "cctv: permissions, secrets, graceful failure without a media box, live stop";

const sfx = Date.now().toString(36).slice(-5);
const need = (v, what) => { if (!v) throw new Error(`skipped: prerequisite missing (${what})`); return v; };

export async function run(rec, { crowd }) {
  await qa.resetClock();
  const admin = await qa.login(DEMO.admin);
  // A pure viewer: a role with cctv.view only (the QA manager also has cctv.manage).
  const role = await admin.post("/api/v1/roles", { name: { fa: `بیننده‌ی دوربین ${sfx}` }, description: "QA S13", permissions: [{ code: "cctv.view", scope: "all" }], version: null });
  const viewerPhone = "0917" + String(Math.floor(1_000_000 + Math.random() * 8_999_999));
  await admin.post("/api/v1/users", { displayName: `بیننده QA ${sfx}`, phone: viewerPhone, roleIds: [role.id], isActive: true });
  const viewer = await qa.login(viewerPhone);
  const noView = await qa.login(DEMO.writer);                   // blog writer: no cctv rights
  const client = await qa.login(pick(crowd, "client", 1, 0)[0]);
  let cam, status;

  status = await rec.step("2.13.a", "admin sees the camera page status (configured flag + cameras)", async () => {
    const s = await admin.get("/api/v1/cctv");
    check(typeof s.configured === "boolean" && Array.isArray(s.cameras), "status shape");
    return s;
  });

  await rec.step("2.13.b", "add camera: ethics confirmation and RTSP address required; password never returned", async () => {
    const body = (over = {}) => ({ name: `دوربین QA ${sfx}`, location: "اتاق تدوین", sortOrder: 9, isActive: true, mainUrl: "rtsp://admin:Sup3rSecret@10.20.30.40:554/Streaming/Channels/101",
      subUrl: "rtsp://admin:Sup3rSecret@10.20.30.40:554/Streaming/Channels/102", workAreaConfirmed: true, version: null, ...over });
    await admin.refused("POST", "/api/v1/cctv/cameras", body({ workAreaConfirmed: false }), [422]);
    await admin.refused("POST", "/api/v1/cctv/cameras", body({ mainUrl: "" }), [422]);
    await admin.refused("POST", "/api/v1/cctv/cameras", body({ mainUrl: "http://evil.example/stream" }), [422]);
    cam = await admin.post("/api/v1/cctv/cameras", body());
    const text = JSON.stringify(cam) + JSON.stringify(await admin.get("/api/v1/cctv"));
    check(!text.includes("Sup3rSecret"), "camera password never sent back");
    check(cam.mainMasked && cam.hasSubstream, `masked address shown to managers: ${cam.mainMasked}`);
    const forViewer = (await viewer.get("/api/v1/cctv")).cameras.find((c) => c.id === cam.id);
    check(forViewer && forViewer.mainMasked === null, "viewers don't even see the masked address");
  });

  await rec.step("2.13.c", "no cctv.view (staff writer, client) → 403 on list, snapshot, live, stop; viewer can't manage", async () => {
    need(cam, "camera");
    for (const a of [noView, client]) {
      await a.refused("GET", "/api/v1/cctv", undefined, [403]);
      await a.refused("GET", `/api/v1/cctv/cameras/${cam.id}/snapshot`, undefined, [403]);
      await a.refused("POST", `/api/v1/cctv/cameras/${cam.id}/live`, {}, [403]);
      await a.refused("POST", `/api/v1/cctv/cameras/${cam.id}/live/stop`, { sessionId: cam.id }, [403]);
    }
    await viewer.refused("POST", "/api/v1/cctv/cameras", { name: "x", location: null, sortOrder: 1, isActive: true, mainUrl: "rtsp://10.0.0.1/a", subUrl: null, workAreaConfirmed: true }, [403]);
    await viewer.refused("DELETE", `/api/v1/cctv/cameras/${cam.id}`, undefined, [403]);
    const anon = await qa.anon("GET", "/api/v1/cctv");
    eq(anon.status, 401, "anonymous → 401");
  });

  let live;
  await rec.step("2.13.d", "snapshot without a working media box → clear 409 (not 500), quickly", async () => {
    need(cam, "camera");
    const t0 = Date.now();
    const r = await qa.anon("GET", `/api/v1/cctv/cameras/${cam.id}/snapshot`, undefined, { token: viewer.token });
    eq(r.status, 409, `snapshot status (body ${JSON.stringify(r.data).slice(0, 200)})`);
    check(/cctv\.(notConfigured|noFrame)/.test(JSON.stringify(r.data)) || typeof r.data?.detail === "string", "problem details with a reason");
    check(Date.now() - t0 < 12_000, "answers within the 8 s ffmpeg limit");
  });

  await rec.step("2.13.e", "live view: not configured → 409; configured → short-lived token for this camera (audited)", async () => {
    need(cam, "camera");
    const r = await qa.anon("POST", `/api/v1/cctv/cameras/${cam.id}/live`, {}, { token: viewer.token });
    if (!status?.configured) {
      eq(r.status, 409, "live refused while the media box is not set up");
      return;
    }
    eq(r.status, 200, "live token issued");
    live = r.data;
    check(live.token && live.whepUrl && live.idleSeconds === 120, `live dto ${JSON.stringify(live).slice(0, 200)}`);
    check(new Date(live.expiresAt) - Date.now() <= 5 * 60_000 + 60_000, "token lives ~5 minutes");
  });

  await rec.step("2.13.f", "idle stop endpoint: stopping a live view is accepted and audited (also for an unknown session)", async () => {
    need(cam, "camera");
    await viewer.post(`/api/v1/cctv/cameras/${cam.id}/live/stop`, { sessionId: live?.sessionId ?? "00000000-0000-7000-8000-000000000001" });
  });

  await rec.step("2.13.g", "resync without a media box → 409; media-auth hook refuses callers outside the tunnel", async () => {
    const r = await qa.anon("POST", "/api/v1/cctv/resync", {}, { token: admin.token });
    if (!status?.configured) eq(r.status, 409, "resync refused when not set up");
    else check(r.status === 200, `resync answers even with the box down (got ${r.status})`);
    const hook = await qa.anon("POST", "/api/v1/cctv/media-auth", { user: null, password: null, token: "x", ip: "1.2.3.4", action: "read", path: "cam", protocol: "webrtc", id: null, query: "" });
    eq(hook.status, 403, "hook only from the box's tunnel address");
  });

  await rec.step("2.13.h", "deactivate → hidden from viewers and no live; delete → 404", async () => {
    need(cam, "camera");
    const off = await admin.put(`/api/v1/cctv/cameras/${cam.id}`, { name: cam.name, location: cam.location, sortOrder: 9, isActive: false, mainUrl: null, subUrl: null, workAreaConfirmed: true, version: cam.version });
    eq(off.isActive, false, "inactive");
    check(!(await viewer.get("/api/v1/cctv")).cameras.some((c) => c.id === cam.id), "hidden from viewers");
    await viewer.refused("POST", `/api/v1/cctv/cameras/${cam.id}/live`, {}, [404]);
    await admin.del(`/api/v1/cctv/cameras/${cam.id}`);
    await admin.refused("PUT", `/api/v1/cctv/cameras/${cam.id}`, { name: "x", location: null, sortOrder: 1, isActive: true, mainUrl: null, subUrl: null, workAreaConfirmed: true }, [404]);
    await viewer.refused("GET", `/api/v1/cctv/cameras/${cam.id}/snapshot`, undefined, [404]);
  });
}
