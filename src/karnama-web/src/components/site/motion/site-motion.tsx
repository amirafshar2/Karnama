"use client";

import { useEffect } from "react";

/**
 * The public site's only always-on script (≈1 KB): one IntersectionObserver marks `[data-reveal]` / `[data-observe]`
 * elements as they enter the screen (CSS does the animating), and `[data-tl]` timelines get their progress line
 * from the scroll position (21st: @ibrandify/scroll-timeline). Without JavaScript everything is simply visible.
 * It lives in the site layout, which stays mounted while visitors move between pages, so it also watches the page for
 * new content: without that, cards on a page opened from the menu (plans, blog, cases) were never revealed and stayed invisible.
 */
export function SiteMotion() {
  useEffect(() => {
    const root = document.querySelector(".site");
    root?.classList.remove("static");
    const seen = new IntersectionObserver(
      (entries) => {
        for (const e of entries) {
          if (!e.isIntersecting) continue;
          (e.target as HTMLElement).setAttribute("data-in", "");
          seen.unobserve(e.target);
        }
      },
      { rootMargin: "0px 0px -12% 0px", threshold: 0.12 },
    );
    let lines: HTMLElement[] = [];
    let raf = 0;
    let pending = 0;
    const watch = () => {
      pending = 0;
      document.querySelectorAll("[data-reveal]:not([data-in]), [data-observe]:not([data-in])").forEach((el) => seen.observe(el));
      lines = [...document.querySelectorAll<HTMLElement>("[data-tl]")];
      if (lines.length) onScroll();
    };
    // New page content (client-side navigation, streamed sections): look again, at most once per frame.
    const changes = new MutationObserver(() => {
      if (!pending) pending = requestAnimationFrame(watch);
    });
    changes.observe(document.body, { childList: true, subtree: true });
    const paint = () => {
      raf = 0;
      const vh = window.innerHeight;
      for (const el of lines) {
        const r = el.getBoundingClientRect();
        const p = Math.min(1, Math.max(0, (vh * 0.6 - r.top) / r.height));
        el.style.setProperty("--tl", p.toFixed(3));
      }
    };
    const onScroll = () => {
      if (!raf) raf = requestAnimationFrame(paint);
    };
    window.addEventListener("scroll", onScroll, { passive: true });
    watch();
    return () => {
      seen.disconnect();
      changes.disconnect();
      cancelAnimationFrame(pending);
      window.removeEventListener("scroll", onScroll);
      cancelAnimationFrame(raf);
    };
  }, []);
  return null;
}
