export const API_BASE = (process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:5080").replace(/\/$/, "");
export const HUB_BASE = (process.env.NEXT_PUBLIC_HUB_BASE ?? API_BASE).replace(/\/$/, "");
/** "android" inside the Capacitor app (static export), "web" otherwise. */
export const CLIENT_KIND: "web" | "android" = process.env.NEXT_PUBLIC_CLIENT === "android" ? "android" : "web";

/** Public media ("/media/public/…"): the website reaches it through its own origin (rewrite); the app asks the API directly. */
export const mediaUrl = (path: string) => (CLIENT_KIND === "android" || !path.startsWith("/") ? (path.startsWith("/") ? API_BASE + path : path) : path);

/** Where the Android app (APK) is downloaded from, e.g. a GitHub release asset. Empty = "not ready yet" on the download page. */
export const APK_URL = (process.env.NEXT_PUBLIC_APK_URL ?? "").trim();
