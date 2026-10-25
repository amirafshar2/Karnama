// Load test (docs/PROMPT.md M7): 1000 concurrent users for 5 minutes against a STAGING install in demo mode.
//   k6 run -e API=https://api.staging.example.ir -e SITE=https://staging.example.ir tests/load/k6-1000.js
// Pass criteria (thresholds below): p95 < 800 ms for the API, < 1.5 s for public pages, errors < 1%.
import http from "k6/http";
import { check, sleep } from "k6";

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
  sleep(3 + Math.random() * 4);
}

export function visitor() {
  const p = { tags: { kind: "page" } };
  check(http.get(`${SITE}/`, p), { home: (r) => r.status === 200 });
  http.get(`${SITE}/blog`, p);
}
