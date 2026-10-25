// Load test (docs/PROMPT.md M7): 1000 concurrent users for 5 minutes against a STAGING install in demo mode.
//   k6 run -e API=https://api.staging.example.ir -e SITE=https://staging.example.ir tests/load/k6-1000.js
// Pass criteria (thresholds below): p95 < 800 ms for the API, < 1.5 s for public pages, errors < 1%.
// N12 (PROMPT2 §16): also chat (list, read, send ~1 in 10 visits), a small photo upload in chat, equipment, help and reports.
import http from "k6/http";
import { check, sleep } from "k6";
import encoding from "k6/encoding";

// 1×1 PNG: exercises the upload path (signature sniffing, private store) without measuring bandwidth.
const PNG = encoding.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

const API = __ENV.API;
const SITE = __ENV.SITE;
const ROLES = ["admin", "editor", "writer", "client"];

export const options = {
  scenarios: {
    staff: { executor: "ramping-vus", startVUs: 0, stages: [{ duration: "1m", target: 700 }, { duration: "3m", target: 700 }, { duration: "1m", target: 0 }], exec: "staff" },
    visitors: { executor: "constant-arrival-rate", rate: 50, timeUnit: "1s", duration: "5m", preAllocatedVUs: 300, exec: "visitor" },
  },
  thresholds: {
    "http_req_failed": ["rate<0.01"],
    "http_req_duration{kind:api}": ["p(95)<800"],
    "http_req_duration{kind:page}": ["p(95)<1500"],
  },
};

export function setup() {
  // Demo login (staging only): one token per role, shared by the virtual users.
  const tokens = {};
  for (const role of ROLES) {
    const r = http.post(`${API}/api/v1/auth/demo`, JSON.stringify({ role }), { headers: { "Content-Type": "application/json", "X-Client": "android" } });
    tokens[role] = r.json("accessToken");
  }
  return tokens;
}

export function staff(tokens) {
  const role = ROLES[__VU % 3];
  const h = { headers: { Authorization: `Bearer ${tokens[role]}` }, tags: { kind: "api" } };
  check(http.get(`${API}/api/v1/me`, h), { me: (r) => r.status === 200 });
  http.get(`${API}/api/v1/notifications`, h);
  http.get(`${API}/api/v1/changes`, h);
  if (role !== "writer") http.get(`${API}/api/v1/work/board`, h);
  http.get(`${API}/api/v1/attendance/me`, h);

  // Chat: the list every visit, one conversation's latest page, and sometimes a message or a photo.
  const chats = http.get(`${API}/api/v1/chat`, h);
  const first = chats.status === 200 ? chats.json("0.id") : null;
  if (first) {
    http.get(`${API}/api/v1/chat/${first}/messages?limit=40`, h);
    if (Math.random() < 0.1) {
      const body = JSON.stringify({ body: `k6 ${__VU}-${__ITER}`, clientKey: uuid() });
      check(http.post(`${API}/api/v1/chat/${first}/messages`, body, { headers: { ...h.headers, "Content-Type": "application/json" }, tags: { kind: "api" } }), { sent: (r) => r.status === 200 });
    }
    if (Math.random() < 0.02) {
      const form = { file: http.file(PNG, "k6.png", "image/png") };
      check(http.post(`${API}/api/v1/chat/${first}/files?clientKey=${uuid()}`, form, h), { photo: (r) => r.status === 200 });
    }
  }
  http.get(`${API}/api/v1/assets/mine`, h);
  if (Math.random() < 0.2) http.get(`${API}/api/v1/help`, h);
  if (role === "admin" && Math.random() < 0.1) {
    const to = new Date().toISOString().slice(0, 10);
    const from = new Date(Date.now() - 30 * 864e5).toISOString().slice(0, 10);
    http.get(`${API}/api/v1/reports/work?from=${from}&to=${to}`, h);
  }
  sleep(3 + Math.random() * 4);
}

export function visitor() {
  const p = { tags: { kind: "page" } };
  check(http.get(`${SITE}/`, p), { home: (r) => r.status === 200 });
  http.get(`${SITE}/blog`, p);
}

function uuid() {
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
  });
}
