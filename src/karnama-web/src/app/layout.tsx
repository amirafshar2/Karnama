import type { Metadata, Viewport } from "next";
import localFont from "next/font/local";
import { themeInitScript } from "@/lib/theme";
import "./globals.css";

// Self-hosted (no Google Fonts: may be unreachable in Iran). One variable file covers 100–900.
const vazirmatn = localFont({
  src: "./fonts/Vazirmatn-Variable.woff2",
  variable: "--font-vazirmatn",
  weight: "100 900",
  display: "swap",
  preload: true,
});

export const metadata: Metadata = {
  title: { default: "کارنما", template: "%s · کارنما" },
  description: "مدیریت کار، ساعت کار و سفارش‌های آژانس",
  robots: { index: false, follow: false },
  manifest: "/manifest.webmanifest",
  applicationName: "کارنما",
  appleWebApp: { capable: true, title: "کارنما", statusBarStyle: "black-translucent" },
  icons: {
    icon: [{ url: "/brand/favicon-48.png", sizes: "48x48", type: "image/png" }, { url: "/brand/icon-192.png", sizes: "192x192", type: "image/png" }],
    apple: [{ url: "/brand/apple-touch-icon.png", sizes: "180x180" }],
  },
  formatDetection: { telephone: false },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#f4f4f3" },
    { media: "(prefers-color-scheme: dark)", color: "#0a0a0a" },
  ],
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="fa" dir="rtl" className={`${vazirmatn.variable} h-full antialiased`} suppressHydrationWarning>
      <head>
        {/* Applies the saved light/dark theme before first paint (no flash). */}
        <script dangerouslySetInnerHTML={{ __html: themeInitScript }} />
      </head>
      <body className="min-h-full font-sans">
        {children}
      </body>
    </html>
  );
}
