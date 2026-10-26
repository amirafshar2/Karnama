"use client";

import { useEffect } from "react";

type Conn = { saveData?: boolean; effectiveType?: string };

/**
 * The hero's background loop: attached only after the page has loaded, never with reduced motion, data saver or 2G/3G.
 * (The card fan used to lean toward the cursor; that cost as much per frame as the video itself and felt laggy on
 * ordinary laptops, so the fan now only lifts the card under the mouse — see .fan__card:hover in site.css.)
 */
export function HeroTilt() {
  useEffect(() => {
    const video = document.querySelector<HTMLVideoElement>(".hero__media video[data-src]");
    if (!video) return;
    const conn = (navigator as Navigator & { connection?: Conn }).connection;
    const slow = conn?.saveData || /(^|-)2g|3g/.test(conn?.effectiveType ?? "");
    if (slow || matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const start = () => {
      video.src = video.dataset.src!;
      video.addEventListener("playing", () => (video.style.opacity = "1"), { once: true });
      void video.play().catch(() => {});
    };
    if (document.readyState === "complete") setTimeout(start, 200);
    else window.addEventListener("load", () => setTimeout(start, 200), { once: true });
  }, []);

  return null;
}
