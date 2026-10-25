import type { NextConfig } from "next";

// One codebase, two outputs:
//  - web (default): normal Next.js server build
//  - mobile (BUILD_TARGET=mobile): static export bundled inside the Android app by Capacitor
const isMobile = process.env.BUILD_TARGET === "mobile";

const api = process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:5080";
const hub = process.env.NEXT_PUBLIC_HUB_BASE ?? api;
const ws = (u: string) => u.replace(/^http/, "ws");

// Static pages carry Next's inline bootstrap scripts, so script-src needs 'unsafe-inline' (nonces need
// per-request rendering). Everything else is locked down: no framing, no plugins, API/hub-only connections.
const csp = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline'",
  "style-src 'self' 'unsafe-inline'",
  `img-src 'self' data: blob: ${api}`,
  "font-src 'self'",
  `connect-src 'self' ${api} ${hub} ${ws(hub)}`,
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
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), payment=()" },
  { key: "Cross-Origin-Opener-Policy", value: "same-origin" },
  ...(process.env.NODE_ENV === "production" && api.startsWith("https://")
    ? [{ key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains" }]
    : []),
];

const nextConfig: NextConfig = {
  reactStrictMode: true,
  // Lets the visual test script build the demo and the empty install side by side.
  distDir: process.env.NEXT_DIST_DIR || ".next",
  poweredByHeader: false,
  output: isMobile ? "export" : undefined,
  trailingSlash: isMobile,
  images: { unoptimized: isMobile },
  // Headers apply to the web server build (static export inside the app has no server).
  ...(isMobile ? {} : { headers: async () => [{ source: "/:path*", headers: securityHeaders }] }),
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
