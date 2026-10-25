export const API_BASE = (process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:5080").replace(/\/$/, "");
export const HUB_BASE = (process.env.NEXT_PUBLIC_HUB_BASE ?? API_BASE).replace(/\/$/, "");
/** "android" inside the Capacitor app (static export), "web" otherwise. */
export const CLIENT_KIND: "web" | "android" = process.env.NEXT_PUBLIC_CLIENT === "android" ? "android" : "web";
