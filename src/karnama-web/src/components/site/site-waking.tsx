"use client";

import { useEffect, useState } from "react";
import { API_BASE } from "@/lib/config";
import { wakeApi } from "@/lib/wake";

/**
 * Shown by the public site when its page was rendered without the API (asleep on a free host): wakes the API, waits for it,
 * asks the server to rebuild the pages and reloads, so the visitor sees the full site after about a minute instead of an
 * empty page. Gives up quietly after 3 minutes.
 */
export function SiteWaking({ text }: { text: string }) {
  const [gaveUp, setGaveUp] = useState(false);
  useEffect(() => {
    let stop = false;
    const started = Date.now();
    const tick = async () => {
      while (!stop) {
        wakeApi();
        try {
          const r = await fetch(`${API_BASE}/api/v1/public/brand`, { cache: "no-store" });
          if (r.ok) {
            await fetch("/site-refresh", { method: "POST" }).catch(() => {});
            await new Promise((res) => setTimeout(res, 1500));
            if (!stop) window.location.reload();
            return;
          }
        } catch {
          /* still asleep */
        }
        if (Date.now() - started > 180_000) return setGaveUp(true);
        await new Promise((res) => setTimeout(res, 5000));
      }
    };
    void tick();
    return () => {
      stop = true;
    };
  }, []);
  if (gaveUp) return null;
  return (
    <p role="status" className="site-waking" data-testid="site-waking">
      <span className="site-waking__dot" aria-hidden="true" />
      {text}
    </p>
  );
}
