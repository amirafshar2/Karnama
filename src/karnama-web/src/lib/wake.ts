import { HUB_BASE } from "./config";

let last = 0;

/**
 * Free hosting (Render) puts the API to sleep when idle and only wakes it for requests that reach its own public address
 * from outside. The website's server-side proxy calls it from inside Render, which gets a quick 502 and does NOT wake it,
 * so the visitor's browser knocks on the API's door directly. `no-cors`: we only need the request to arrive, not the
 * answer. No-op where the API shares the site's origin (a normal install) and at most once every 20 seconds.
 */
export function wakeApi() {
  if (typeof window === "undefined" || !/^https?:\/\//.test(HUB_BASE)) return;
  if (new URL(HUB_BASE).origin === window.location.origin || Date.now() - last < 20_000) return;
  last = Date.now();
  fetch(`${HUB_BASE}/health/live`, { mode: "no-cors", cache: "no-store" }).catch(() => {});
}
