// QA scenario toolkit (docs/PROMPT3.md Q1). Plain Node 22, no dependencies.
// Every scenario talks to a QA-mode API (Qa__Enabled=true): movable clock, /api/v1/qa/login-as, job triggers, invariants.
import { randomUUID } from "node:crypto";

export const API = (process.env.QA_API ?? "http://localhost:5090").replace(/\/$/, "");
export const OFFICE_IP = "127.0.0.1";
export const HOME_IP = "5.120.33.44";

export class HttpError extends Error {
  constructor(status, body, method, path) {
    super(`${method} ${path} → ${status} ${typeof body === "string" ? body.slice(0, 300) : JSON.stringify(body).slice(0, 300)}`);
    this.status = status; this.body = body;
  }
}

async function raw(method, path, { token, body, ip, android, headers = {}, form } = {}) {
  const h = { Accept: "application/json", ...headers };
  if (token) h.Authorization = `Bearer ${token}`;
  if (ip && ip !== OFFICE_IP) h["X-Forwarded-For"] = ip; // the QA API trusts 127.0.0.1 as a proxy
  if (android) h["X-Client"] = "android";
  let payload;
  if (form) payload = form;
  else if (body !== undefined) { h["Content-Type"] = "application/json"; payload = JSON.stringify(body); }
  const res = await fetch(API + path, { method, headers: h, body: payload });
  const text = await res.text();
  let data = text;
  try { data = text ? JSON.parse(text) : null; } catch { /* not JSON */ }
  return { status: res.status, data, headers: res.headers };
}

/** A signed-in person. `ip` simulates where they are (office by default). */
export class Actor {
  constructor(person, session, opts = {}) { this.person = person; this.phone = person.phone; this.name = person.name; this.token = session.accessToken; this.ip = opts.ip ?? OFFICE_IP; this.android = !!opts.android; }
  async call(method, path, body, opts = {}) {
    const r = await raw(method, path, { token: this.token, body, ip: opts.ip ?? this.ip, android: this.android, headers: opts.headers, form: opts.form });
    const expect = opts.expect ?? [200, 201, 202, 204];
    if (!expect.includes(r.status)) throw new HttpError(r.status, r.data, method, path);
    return r.data;
  }
  get(p, o) { return this.call("GET", p, undefined, o); }
  post(p, b, o) { return this.call("POST", p, b ?? {}, o); }
  put(p, b, o) { return this.call("PUT", p, b, o); }
  patch(p, b, o) { return this.call("PATCH", p, b, o); }
  del(p, o) { return this.call("DELETE", p, undefined, o); }
  /** Pages the bell (30 per page) until a notification matches; a busy manager may have more than 30 since this morning. */
  async hasNotification(pred, pages = 6) {
    let before = null;
    for (let i = 0; i < pages; i++) {
      const page = await this.get("/api/v1/notifications" + (before ? `?before=${encodeURIComponent(before)}` : ""));
      if (page.items.some(pred)) return true;
      if (page.items.length < 30) return false;
      before = page.items[page.items.length - 1].createdAt;
    }
    return false;
  }
  /** Clocks in unless already in (scenarios share one simulated day, so an earlier scenario may have clocked this person in). */
  async ensureClockedIn(body = {}) {
    const r = await raw("POST", "/api/v1/attendance/clock-in", { token: this.token, body: { idempotencyKey: randomUUID(), ...body }, ip: this.ip, android: this.android });
    if (r.status !== 200 && !(r.status === 409 && r.data?.code === "attendance.alreadyIn")) throw new HttpError(r.status, r.data, "POST", "/api/v1/attendance/clock-in");
    return r.data;
  }
  /** Expects a refusal (4xx); returns the status. */
  async refused(method, path, body, codes = [400, 403, 404, 409, 422]) {
    const r = await raw(method, path, { token: this.token, body, ip: this.ip, android: this.android });
    if (!codes.includes(r.status)) throw new HttpError(r.status, r.data, `${method} (expected ${codes.join("/")})`, path);
    return r.status;
  }
}

export const qa = {
  async crowd() { return (await raw("POST", "/api/v1/qa/seed-crowd")).data.people; },
  async login(personOrPhone, opts = {}) {
    const person = typeof personOrPhone === "string" ? { phone: personOrPhone, name: personOrPhone, group: "?" } : personOrPhone;
    const r = await raw("POST", "/api/v1/qa/login-as", { body: { phone: person.phone }, android: opts.android });
    if (r.status !== 200) throw new HttpError(r.status, r.data, "POST", "/api/v1/qa/login-as");
    return new Actor(person, r.data, opts);
  },
  async clock() { return (await raw("GET", "/api/v1/qa/clock")).data; },
  async advance(minutes) { return (await raw("POST", "/api/v1/qa/clock", { body: { advanceMinutes: minutes } })).data; },
  async setClock(iso) { return (await raw("POST", "/api/v1/qa/clock", { body: { set: iso } })).data; },
  async resetClock() { return (await raw("POST", "/api/v1/qa/clock", { body: { reset: true } })).data; },
  async job(name) { const r = await raw("POST", `/api/v1/qa/jobs/${name}`); if (r.status !== 200) throw new HttpError(r.status, r.data, "POST", name); return r.data.count; },
  async invariants() { return (await raw("GET", "/api/v1/qa/invariants")).data; },
  anon: (method, path, body, opts = {}) => raw(method, path, { body, ...opts }),
  /** Tehran wall-clock time today at hh:mm, as ISO (for qa.setClock). */
  tehran(hh, mm = 0, dayOffset = 0) {
    const now = new Date();
    const t = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + dayOffset, hh, mm) - 3.5 * 3600_000);
    return t.toISOString();
  },
};

export const key = () => randomUUID();
export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Assertion helpers that produce readable Persian+English failures in the report. */
export function check(cond, message) { if (!cond) throw new Error(`CHECK FAILED: ${message}`); }
export function eq(actual, expected, message) { if (actual !== expected) throw new Error(`CHECK FAILED: ${message} (expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)})`); }

/** Records the steps of one scenario: each step has a coverage id from docs/qa/COVERAGE.md. */
export class Recorder {
  constructor(scenario) { this.scenario = scenario; this.steps = []; }
  async step(id, title, fn) {
    const t0 = performance.now();
    try { const v = await fn(); this.steps.push({ id, title, ok: true, ms: Math.round(performance.now() - t0) }); return v; }
    catch (e) { this.steps.push({ id, title, ok: false, ms: Math.round(performance.now() - t0), error: String(e?.message ?? e).slice(0, 800) }); return undefined; }
  }
  get ok() { return this.steps.every((s) => s.ok); }
}

/** Picks people from the crowd by group (lead/office/field/marketer/writer/kiosk/client). */
export function pick(crowd, group, n = 1, offset = 0) { return crowd.filter((p) => p.group === group).slice(offset, offset + n); }
export const DEMO = { admin: "09000000001", editor: "09000000002", writer: "09000000003", client: "09000000004", marketer: "09000000008", owner: "09000000000" };
