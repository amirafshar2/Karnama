"use client";

/**
 * Native adapters (docs/MOBILE.md): pages call these, never a Capacitor plugin directly. In the Android app they use
 * the plugins; in the browser they return null so the page uses its web path. The plugins are imported lazily and only
 * when CLIENT_KIND is "android" (a build-time constant), so the website never downloads them.
 */
import { CLIENT_KIND } from "./config";

export const isNative = CLIENT_KIND === "android";

// ---- Secure storage (Android Keystore) for the refresh token ----
export async function secureGet(key: string): Promise<string | null> {
  if (!isNative) return null;
  const { SecureStorage } = await import("@aparajita/capacitor-secure-storage");
  try {
    return await SecureStorage.getItem(key);
  } catch {
    return null;
  }
}

export async function secureSet(key: string, value: string | null) {
  if (!isNative) return;
  const { SecureStorage } = await import("@aparajita/capacitor-secure-storage");
  if (value === null) await SecureStorage.removeItem(key).catch(() => undefined);
  else await SecureStorage.setItem(key, value);
}

// ---- Location at clock-in (one reading) + fake-GPS detection ----
export type NativeFix = { latitude: number; longitude: number; accuracy: number; mock: boolean };

export async function nativeLocate(): Promise<NativeFix | null> {
  if (!isNative) return null;
  const [{ Geolocation }, { MockLocationDetector }] = await Promise.all([import("@capacitor/geolocation"), import("@capgo/capacitor-mock-location-detector")]);
  try {
    const perm = await Geolocation.requestPermissions({ permissions: ["location"] });
    if (perm.location !== "granted") return null;
    const [pos, integrity] = await Promise.all([
      Geolocation.getCurrentPosition({ enableHighAccuracy: true, timeout: 10_000, maximumAge: 0 }),
      MockLocationDetector.analyze({ checks: ["system_mock_flag", "mock_location_app", "location_anomaly"], requestLocationSample: false }).catch(() => null),
    ]);
    // Developer options alone don't mean cheating; a mock flag, a fake-GPS app or an impossible jump do.
    const mock = !!integrity?.checks.some((c) => c.detected && c.id !== "developer_options" && c.id !== "adb_enabled");
    return { latitude: pos.coords.latitude, longitude: pos.coords.longitude, accuracy: pos.coords.accuracy, mock };
  } catch {
    return null;
  }
}

// ---- QR scan with the phone's scanner (Google code scanner); null = use the in-page camera instead ----
export async function nativeScanQr(): Promise<string | null> {
  if (!isNative) return null;
  const { BarcodeScanner, BarcodeFormat } = await import("@capacitor-mlkit/barcode-scanning");
  try {
    const { available } = await BarcodeScanner.isGoogleBarcodeScannerModuleAvailable();
    if (!available) {
      void BarcodeScanner.installGoogleBarcodeScannerModule().catch(() => undefined);
      return null;
    }
    const { barcodes } = await BarcodeScanner.scan({ formats: [BarcodeFormat.QrCode] });
    return barcodes[0]?.rawValue ?? null;
  } catch {
    return null;
  }
}

// ---- Opening links / downloads outside the app ----
/** In the app a link to another host opens in the system browser (which hands files to Android's download manager). */
export function openOutside(url: string) {
  if (isNative) window.location.href = url;
  else window.open(url, "_blank", "noopener,noreferrer");
}

// ---- Local reminders (publish time for pages the client runs) ----
export async function scheduleReminders(items: { id: string; at: string; title: string; body: string }[]) {
  if (!isNative) return;
  const { LocalNotifications } = await import("@capacitor/local-notifications");
  try {
    const perm = await LocalNotifications.checkPermissions();
    if (perm.display !== "granted" && (await LocalNotifications.requestPermissions()).display !== "granted") return;
    const pending = await LocalNotifications.getPending();
    if (pending.notifications.length) await LocalNotifications.cancel({ notifications: pending.notifications.map((n) => ({ id: n.id })) });
    const now = Date.now();
    const future = items.filter((i) => new Date(i.at).getTime() > now).slice(0, 40);
    if (!future.length) return;
    await LocalNotifications.schedule({
      notifications: future.map((i) => ({
        id: hash(i.id),
        title: i.title,
        body: i.body,
        schedule: { at: new Date(i.at), allowWhileIdle: true },
        extra: { link: "/dashboard" },
      })),
    });
  } catch {
    /* reminders also come by bot/SMS */
  }
}
const hash = (s: string) => [...s].reduce((h, c) => (Math.imul(31, h) + c.charCodeAt(0)) | 0, 7) & 0x7fffffff;

// ---- App shell: Android back button, status bar, splash ----
export async function initNative(navigate: { back: () => void; push: (href: string) => void }) {
  if (!isNative) return () => undefined;
  const [{ App }, { SplashScreen }, { StatusBar, Style }, { LocalNotifications }] = await Promise.all([
    import("@capacitor/app"),
    import("@capacitor/splash-screen"),
    import("@capacitor/status-bar"),
    import("@capacitor/local-notifications"),
  ]);
  await StatusBar.setStyle({ style: Style.Dark }).catch(() => undefined);
  await SplashScreen.hide().catch(() => undefined);
  const back = await App.addListener("backButton", ({ canGoBack }) => {
    // An open sheet/dialog closes first (it listens for Escape); then history; at the root the app goes to background.
    const dialog = document.querySelector("[role=dialog]");
    if (dialog) document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    else if (canGoBack && location.pathname !== "/dashboard") navigate.back();
    else void App.minimizeApp();
  });
  const tap = await LocalNotifications.addListener("localNotificationActionPerformed", (e) => {
    const link = e.notification.extra?.link;
    if (typeof link === "string" && link.startsWith("/") && !link.startsWith("//")) navigate.push(link);
  });
  return () => {
    void back.remove();
    void tap.remove();
  };
}
