import type { CapacitorConfig } from "@capacitor/cli";

/**
 * The Android app is the same Next.js code as the website (static export: `npm run build:web`),
 * talking to the same API. White-label: change appId/appName per agency before publishing.
 */
const config: CapacitorConfig = {
  appId: process.env.KARNAMA_APP_ID ?? "ir.karnama.app",
  appName: process.env.KARNAMA_APP_NAME ?? "کارنما",
  webDir: "../src/karnama-web/out",
  android: {
    // Release builds never talk plain HTTP and never allow WebView debugging.
    allowMixedContent: false,
    webContentsDebuggingEnabled: process.env.KARNAMA_DEBUG === "1",
    captureInput: true,
  },
  server: {
    androidScheme: "https",
    // Only the API host may be loaded inside the app; any other link opens in the system browser.
    allowNavigation: [],
  },
  plugins: {
    SplashScreen: { launchShowDuration: 600, backgroundColor: "#0a0a0a", showSpinner: false, androidScaleType: "CENTER_CROP" },
    StatusBar: { style: "DARK", backgroundColor: "#0a0a0a", overlaysWebView: false },
    LocalNotifications: { smallIcon: "ic_stat_karnama", iconColor: "#feff00" },
  },
};

export default config;
