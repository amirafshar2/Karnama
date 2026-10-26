"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";

export type ReelItem = { href: string; title: string; meta: string; number: string | null; tag: string | null; image: string | null };

/**
 * Hover Reel (21st: @ham7a311/hover-reel): a list of projects; pointing at a row blooms its photo under the cursor,
 * and moving to another row rolls the photos past inside the frame. Touch screens show the photo inline instead.
 */
export function HoverReel({ items }: { items: ReelItem[] }) {
  const host = useRef<HTMLDivElement>(null);
  const thumb = useRef<HTMLDivElement>(null);
  const [active, setActive] = useState(0);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const el = host.current;
    const t = thumb.current;
    if (!el || !t) return;
    const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
    const s = { x: 0, y: 0, tx: 0, ty: 0, raf: 0, placed: false };
    const write = () => (t.style.translate = `${s.x}px ${s.y}px`);
    const tick = () => {
      s.raf = 0;
      const k = reduced ? 1 : 0.14;
      s.x += (s.tx - s.x) * k;
      s.y += (s.ty - s.y) * k;
      write();
      if (Math.abs(s.tx - s.x) + Math.abs(s.ty - s.y) > 0.3) s.raf = requestAnimationFrame(tick);
    };
    const move = (e: PointerEvent) => {
      if (e.pointerType !== "mouse") return;
      const r = el.getBoundingClientRect();
      s.tx = Math.min(r.width - t.offsetWidth, Math.max(0, e.clientX - r.left - t.offsetWidth / 2));
      s.ty = Math.min(r.height - t.offsetHeight / 2, Math.max(-t.offsetHeight / 2, e.clientY - r.top - t.offsetHeight / 2));
      if (!s.placed) {
        s.x = s.tx;
        s.y = s.ty;
        s.placed = true;
        write();
      }
      if (!s.raf) s.raf = requestAnimationFrame(tick);
    };
    const leave = () => {
      s.placed = false;
      setOpen(false);
    };
    el.addEventListener("pointermove", move);
    el.addEventListener("pointerleave", leave);
    return () => {
      cancelAnimationFrame(s.raf);
      el.removeEventListener("pointermove", move);
      el.removeEventListener("pointerleave", leave);
    };
  }, []);

  return (
    <div ref={host} className="reel">
      <ul className="reel__list" data-testid="cases-reel">
        {items.map((p, i) => (
          <li key={p.href}>
            <Link
              href={p.href}
              className="reel__row"
              onPointerEnter={(e) => {
                if (e.pointerType !== "mouse") return;
                setActive(i);
                setOpen(true);
              }}
            >
              {p.image && (
                <span className="reel__inline" aria-hidden="true">
                  {/* eslint-disable-next-line @next/next/no-img-element -- uploaded photo */}
                  <img src={p.image} alt="" loading="lazy" />
                </span>
              )}
              <span className="reel__title">
                {p.title}
                {p.tag && <span className="reel__tag">{p.tag}</span>}
              </span>
              {p.number ? <span className="reel__num">{p.number}</span> : <span />}
              <span className="reel__meta">{p.meta}</span>
            </Link>
          </li>
        ))}
      </ul>
      <div ref={thumb} className="reel__thumb" data-open={open ? "" : undefined} aria-hidden="true">
        <div className="reel__strip" style={{ translate: `0 ${-100 * active}%` }}>
          {items.map((p) => (
            <div className="reel__shot" key={p.href}>
              {/* eslint-disable-next-line @next/next/no-img-element -- uploaded photo */}
              {p.image ? <img src={p.image} alt="" loading="lazy" /> : <div className="size-full bg-[var(--hi)]" />}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
