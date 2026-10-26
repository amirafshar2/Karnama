"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";

type Item = { href: string; label: string };

/**
 * Morphing scroll navbar (21st: @laziekiki/morphing-scroll-navbar): a full-width transparent header over the hero
 * that folds into a blurred floating capsule once the page scrolls. Pages without a dark hero start as the capsule.
 */
export function SiteNav({ brand, items, cta, login, menu, close }: { brand: React.ReactNode; items: Item[]; cta: Item; login: Item; menu: string; close: string }) {
  const path = usePathname();
  const [scrolled, setScrolled] = useState(false);
  // The menu is open only for the page it was opened on: navigating closes it without an effect.
  const [openOn, setOpenOn] = useState<string | null>(null);
  const open = openOn === path;
  const setOpen = (v: boolean) => setOpenOn(v ? path : null);
  const solid = path !== "/";

  useEffect(() => {
    const on = () => setScrolled(window.scrollY > 40);
    on();
    window.addEventListener("scroll", on, { passive: true });
    return () => window.removeEventListener("scroll", on);
  }, []);
  useEffect(() => {
    if (!open) return;
    const esc = (e: KeyboardEvent) => e.key === "Escape" && setOpenOn(null);
    document.addEventListener("keydown", esc);
    return () => document.removeEventListener("keydown", esc);
  }, [open]);

  return (
    <header className="snav" data-scrolled={scrolled ? "" : undefined} data-solid={solid ? "" : undefined}>
      <div className="snav__bar">
        <Link href="/" className="rounded-full px-2 focus-visible:outline-2">
          {brand}
        </Link>
        <nav className="snav__links flex items-center gap-1" aria-label={menu}>
          {items.map((i) => (
            <Link key={i.href} href={i.href} className="snav__link" aria-current={path.startsWith(i.href) ? "page" : undefined}>
              {i.label}
            </Link>
          ))}
        </nav>
        <Link href={cta.href} className="snav__cta">
          {cta.label}
        </Link>
        <Link href={login.href} className="snav__login">
          {login.label}
        </Link>
        <button type="button" className="snav__menu" aria-expanded={open} aria-controls="site-sheet" aria-label={menu} onClick={() => setOpen(true)}>
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" aria-hidden="true">
            <path d="M4 7h16M4 12h16M4 17h10" />
          </svg>
        </button>
      </div>
      <div id="site-sheet" className="snav__sheet" hidden={!open} role="dialog" aria-modal="true" aria-label={menu} style={{ pointerEvents: "auto" }}>
        <button type="button" onClick={() => setOpen(false)} className="absolute top-6 end-6 grid size-12 place-items-center rounded-full border border-white/25 text-2xl" aria-label={close}>
          ×
        </button>
        {[...items, login].map((i) => (
          <Link key={i.href} href={i.href} onClick={() => setOpen(false)}>
            {i.label}
          </Link>
        ))}
        <Link href={cta.href} onClick={() => setOpen(false)} className="mt-6 !text-[var(--hi-text)]">
          {cta.label}
        </Link>
      </div>
    </header>
  );
}
