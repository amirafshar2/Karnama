"use client";

import { useEffect } from "react";

type Conn = { saveData?: boolean; effectiveType?: string };

/**
 * Pointer parallax for the hero's card fan (the whole fan leans a few degrees toward the cursor, mouse only),
 * and the background loop: attached only after the page has loaded, never with reduced motion, data saver or 2G/3G.
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

  useEffect(() => {
    const fan = document.querySelector<HTMLElement>("[data-fan]");
    if (!fan || matchMedia("(prefers-reduced-motion: reduce)").matches || !matchMedia("(hover: hover)").matches) return;
    let raf = 0;
    let x = 0;
    let y = 0;
    const paint = () => {
      raf = 0;
      fan.style.transform = `rotateY(${x * 6}deg) rotateX(${-y * 4}deg)`;
    };
    const move = (e: PointerEvent) => {
      x = e.clientX / window.innerWidth - 0.5;
      y = e.clientY / window.innerHeight - 0.5;
      if (!raf) raf = requestAnimationFrame(paint);
    };
    fan.style.transition = "transform 0.6s cubic-bezier(0.22, 1, 0.36, 1)";
    fan.style.transformStyle = "preserve-3d";
    window.addEventListener("pointermove", move, { passive: true });
    return () => {
      window.removeEventListener("pointermove", move);
      cancelAnimationFrame(raf);
    };
  }, []);
  return null;
}
