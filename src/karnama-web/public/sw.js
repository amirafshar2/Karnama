/* Karnama service worker (web / iPhone home-screen app only; the Android app ships its UI inside the APK).
 * - App shell (/_next/static, fonts, icons): cache-first — files are content-hashed, so they never go stale.
 * - Pages: network-first, falling back to the cached copy, then to /offline.html.
 * - API and live connections are never cached (always fresh data, no stale permissions). */
const VERSION = "karnama-v1";
const SHELL = ["/offline.html", "/brand/icon-192.png"];

self.addEventListener("install", (e) => {
  e.waitUntil(caches.open(VERSION).then((c) => c.addAll(SHELL)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (e) => {
  e.waitUntil(
    caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== VERSION).map((k) => caches.delete(k)))).then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (e) => {
  const req = e.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return; // API, hub, logo on the API origin: straight to network

  if (url.pathname.startsWith("/_next/static/") || url.pathname.startsWith("/brand/") || /\.(woff2|png|webp|svg)$/.test(url.pathname)) {
    e.respondWith(
      caches.match(req).then(
        (hit) =>
          hit ||
          fetch(req).then((res) => {
            if (res.ok) caches.open(VERSION).then((c) => c.put(req, res.clone()));
            return res;
          }),
      ),
    );
    return;
  }

  if (req.mode === "navigate") {
    e.respondWith(
      fetch(req)
        .then((res) => {
          if (res.ok) caches.open(VERSION).then((c) => c.put(req, res.clone()));
          return res;
        })
        .catch(() => caches.match(req).then((hit) => hit || caches.match("/offline.html"))),
    );
  }
});
