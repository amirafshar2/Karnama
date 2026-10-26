import type { NextConfig } from "next";

// One codebase, two outputs:
//  - web (default): normal Next.js server build
//  - mobile (BUILD_TARGET=mobile): static export bundled inside the Android app by Capacitor
const isMobile = process.env.BUILD_TARGET === "mobile";

const api = process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:5080";
const hub = process.env.NEXT_PUBLIC_HUB_BASE ?? api;
// N11: live camera video (WebRTC/WHEP) is served from the CCTV subdomain through our reverse proxy.
const cctv = process.env.NEXT_PUBLIC_CCTV_BASE ?? "";
const ws = (u: string) => u.replace(/^http/, "ws");

// Static pages carry Next's inline bootstrap scripts, so script-src needs 'unsafe-inline' (nonces need
// per-request rendering). Everything else is locked down: no framing, no plugins, API/hub-only connections.
const csp = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline'",
  "style-src 'self' 'unsafe-inline'",
  `img-src 'self' data: blob: ${api}`,
  "font-src 'self'",
  // Chat voice messages and private previews play from blob: URLs.
  `media-src 'self' blob: ${api}`,
  `connect-src 'self' ${api} ${hub} ${ws(hub)}${cctv ? " " + cctv : ""}`,
  "object-src 'none'",
  "base-uri 'self'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join("; ");

const securityHeaders = [
  { key: "Content-Security-Policy", value: csp },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  // Own origin only: QR + selfie (camera), chat voice (microphone), attendance (location); never inside frames.
  { key: "Permissions-Policy", value: "camera=(self), microphone=(self), geolocation=(self), payment=()" },
  { key: "Cross-Origin-Opener-Policy", value: "same-origin" },
  ...(process.env.NODE_ENV === "production" && api.startsWith("https://")
    ? [{ key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains" }]
    : []),
];

const nextConfig: NextConfig = {
  reactStrictMode: true,
  // Lets the visual test script build the demo and the empty install side by side.
  // The app build exports static files to out/ (Capacitor webDir).
  distDir: isMobile ? "out" : process.env.NEXT_DIST_DIR || ".next",
  poweredByHeader: false,
  output: isMobile ? "export" : undefined,
  // The public site (SSR, *.web.tsx) exists only on the web server; the app build gets the app (*.mobile.tsx for "/").
  pageExtensions: isMobile ? ["mobile.tsx", "mobile.ts", "tsx", "ts"] : ["web.tsx", "web.ts", "tsx", "ts"],
  trailingSlash: isMobile,
  images: { unoptimized: isMobile },
  // Headers apply to the web server build (static export inside the app has no server).
  ...(isMobile
    ? {}
    : {
        headers: async () => [{ source: "/:path*", headers: securityHeaders }],
        // Blog and brand images live on the API; the site serves them from its own origin (same-origin, cacheable).
        rewrites: async () => [{ source: "/media/public/:path*", destination: `${api}/media/public/:path*` }],
      }),
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
