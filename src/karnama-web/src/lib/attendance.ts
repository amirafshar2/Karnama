"use client";

import { api, ApiError } from "./api";
import { isNative, nativeLocate } from "./native";

export type Trust = "green" | "yellow" | "red";
export type Shift = {
  id: string;
  userId: string;
  userName: string | null;
  day: string;
  clockInAt: string;
  clockOutAt: string | null;
  onBreak: boolean;
  breakMinutes: number;
  workedMinutes: number;
  trust: Trust;
  needsReview: boolean;
  reviewReason: string | null;
  offline: boolean;
  lateMinutes: number;
  earlyLeaveMinutes: number;
  earlyLeaveReason: string | null;
  methods: { method: string; passed: boolean; evidence: string }[];
};
export type Today = {
  shift: Shift | null;
  timer: { workItemId: string; title: string; clientName: string; startedAt: string; minutesToday: number } | null;
  workedMinutesToday: number;
  workedMinutesWeek: number;
  schedule: { day: number; start: string; end: string } | null;
  isHoliday: boolean;
  rule: string;
  methods: string[];
  recentShifts: Shift[];
};
export type Evidence = { qr?: string; latitude?: number; longitude?: number; accuracy?: number; mock?: boolean };
export type ClockAction = "clock-in" | "clock-out" | "break/start" | "break/end";

const QUEUE = "karnama.attendance.queue";
// performance.now() is monotonic: changing the phone's clock doesn't change it (only valid within this page session).
const SESSION = Math.random().toString(36).slice(2);
type Queued = { action: ClockAction; key: string; evidence?: Evidence; note?: string; at: number; perf: number; session: string };

function readQueue(): Queued[] {
  try {
    return JSON.parse(localStorage.getItem(QUEUE) ?? "[]");
  } catch {
    return [];
  }
}
function writeQueue(q: Queued[]) {
  try {
    localStorage.setItem(QUEUE, JSON.stringify(q));
  } catch {
    /* private mode: the queue lives only in memory for this page */
  }
}

const body = (q: Queued) => ({
  idempotencyKey: q.key,
  evidence: q.evidence,
  note: q.note,
  deviceAt: new Date(q.at).toISOString(),
  elapsedMs: Math.max(0, Math.round(q.session === SESSION ? performance.now() - q.perf : Date.now() - q.at)),
});

/**
 * Sends an attendance action. With no network it is queued with an idempotency key and sent later with how long ago it
 * happened, so the server records the real time (docs/PROMPT.md §6.6 offline).
 */
export async function sendClock(action: ClockAction, evidence?: Evidence, note?: string): Promise<{ today: Today | null; queued: boolean }> {
  const q: Queued = { action, key: crypto.randomUUID().replaceAll("-", ""), evidence, note, at: Date.now(), perf: performance.now(), session: SESSION };
  try {
    return { today: await api<Today>(`/api/v1/attendance/${action}`, { method: "POST", body: body(q) }), queued: false };
  } catch (e) {
    if (e instanceof ApiError && e.status === 0) {
      writeQueue([...readQueue(), q]);
      return { today: null, queued: true };
    }
    throw e;
  }
}

/** Sends queued actions in order; stops at the first network failure. Returns how many were sent. */
export async function flushQueue(): Promise<number> {
  const queue = readQueue();
  let sent = 0;
  for (const q of queue) {
    try {
      await api(`/api/v1/attendance/${q.action}`, { method: "POST", body: body(q) });
    } catch (e) {
      if (e instanceof ApiError && e.status === 0) break;
      // A refused action (e.g. already clocked in) is dropped: the server state is the truth.
    }
    sent++;
  }
  writeQueue(queue.slice(sent));
  return sent;
}

export const queuedCount = () => readQueue().length;

/** One reading of the location, only now (never tracking). */
export async function locate(): Promise<Evidence | null> {
  // Android app: the GPS plugin plus fake-location checks (the server flags "mock" readings for review).
  if (isNative) return nativeLocate();
  return new Promise((resolve) => {
    if (!("geolocation" in navigator)) return resolve(null);
    // A permission prompt left unanswered must not hold the clock-in: give up after 8 s (the shift is then reviewed).
    setTimeout(() => resolve(null), 8000);
    navigator.geolocation.getCurrentPosition(
      (p) => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude, accuracy: p.coords.accuracy }),
      () => resolve(null),
      { enableHighAccuracy: true, timeout: 10_000, maximumAge: 0 },
    );
  });
}

export const hm = (minutes: number) => ({ h: Math.floor(minutes / 60), m: minutes % 60 });
