"use client";

import { Share, SquarePlus, X } from "lucide-react";
import { useEffect, useState, useSyncExternalStore } from "react";
import { CLIENT_KIND } from "@/lib/config";
import { useI18n } from "@/lib/i18n/provider";

/** Registers the service worker on the web build (not inside the Android app, which bundles its UI). */
export function ServiceWorker() {
  useEffect(() => {
    if (CLIENT_KIND !== "web" || process.env.NODE_ENV !== "production" || !("serviceWorker" in navigator)) return;
    navigator.serviceWorker.register("/sw.js").catch(() => undefined);
  }, []);
  return null;
}

const DISMISS_KEY = "karnama.iosInstallHint";
const noop = () => () => undefined;

/** iPhone/iPad in Safari, not yet added to the home screen. iPadOS reports itself as a Mac, hence the touch check. */
function shouldShowIosHint() {
  const ua = navigator.userAgent;
  const ios = /iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1);
  const standalone = matchMedia("(display-mode: standalone)").matches || (navigator as Navigator & { standalone?: boolean }).standalone === true;
  let dismissed = false;
  try {
    dismissed = localStorage.getItem(DISMISS_KEY) === "1";
  } catch {
    /* storage unavailable */
  }
  return ios && !standalone && !dismissed;
}

/** iOS has no install prompt: show the two steps once, so iPhone users get the app-like version (and push on iOS 16.4+). */
export function IosInstallHint() {
  const { t } = useI18n();
  const eligible = useSyncExternalStore(noop, shouldShowIosHint, () => false);
  const [closed, setClosed] = useState(false);
  if (!eligible || closed) return null;

  const close = () => {
    setClosed(true);
    try {
      localStorage.setItem(DISMISS_KEY, "1");
    } catch {
      /* storage unavailable */
    }
  };

  return (
    <div role="note" className="flex items-start gap-3 border-b bg-card px-4 py-3 text-sm" data-testid="ios-install-hint">
      <div className="flex-1 space-y-1">
        <p className="font-bold">{t("pwa.iosTitle")}</p>
        <p className="flex flex-wrap items-center gap-1 text-muted-foreground">
          {t("pwa.iosStep1")} <Share className="inline size-4" aria-label="Share" /> {t("pwa.iosStep2")}{" "}
          <SquarePlus className="inline size-4" aria-hidden="true" /> «{t("pwa.iosAdd")}»
        </p>
      </div>
      <button type="button" onClick={close} className="grid size-11 place-items-center rounded-md hover:bg-muted" aria-label={t("common.close")}>
        <X className="size-5" aria-hidden="true" />
      </button>
    </div>
  );
}
