"use client";

import { ChevronLeft, ChevronRight } from "lucide-react";
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { cn } from "@/lib/utils";
import type { WorkStatus } from "@/lib/work-types";

/**
 * The desktop board: seven columns rarely fit, so moving sideways must be easy.
 * - A bar of column buttons (with counts) jumps to any column; the columns on screen are highlighted.
 * - Arrow buttons page one screen sideways; they fade out at either end.
 * - The empty board background can be dragged with the mouse (cards keep their own drag and drop).
 * - Columns snap into place, the edges fade where more columns hide, and on wide screens the columns stretch to fill.
 * - The board is one screen tall and each column scrolls on its own, so the sideways scrollbar is always in view
 *   (before, a long "done" column pushed it far below the fold).
 */
export function BoardScroller({ columns, counts, children }: { columns: WorkStatus[]; counts: Record<string, number>; children: ReactNode }) {
  const { t, formatNumber } = useI18n();
  const ref = useRef<HTMLDivElement>(null);
  const [visible, setVisible] = useState<Set<string>>(new Set());
  const [edges, setEdges] = useState({ start: false, end: false });
  const pan = useRef<{ x: number; left: number; moved: boolean } | null>(null);
  const [panning, setPanning] = useState(false);
  const [height, setHeight] = useState<number>();

  // Where are we? (RTL: scrollLeft runs 0 → negative.)
  const measure = useCallback(() => {
    const el = ref.current;
    if (!el) return;
    const max = el.scrollWidth - el.clientWidth;
    const pos = Math.abs(el.scrollLeft);
    setEdges({ start: pos > 4, end: pos < max - 4 });
  }, []);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    measure();
    const io = new IntersectionObserver(
      (entries) =>
        setVisible((prev) => {
          const next = new Set(prev);
          for (const e of entries) {
            const id = (e.target as HTMLElement).dataset.col!;
            if (e.intersectionRatio >= 0.6) next.add(id);
            else next.delete(id);
          }
          return next;
        }),
      { root: el, threshold: [0, 0.6, 1] },
    );
    el.querySelectorAll<HTMLElement>("[data-col]").forEach((c) => io.observe(c));
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    // One screen tall: from the board's top to the bottom of the window (at least 24rem).
    const fit = () => setHeight(Math.max(384, Math.round(window.innerHeight - (el.getBoundingClientRect().top + window.scrollY) - 16)));
    fit();
    window.addEventListener("resize", fit);
    return () => {
      io.disconnect();
      ro.disconnect();
      window.removeEventListener("resize", fit);
    };
  }, [measure, children]);

  // Scroll only the board (scrollIntoView would also move the page).
  const jump = (status: WorkStatus) => {
    const el = ref.current;
    const col = el?.querySelector<HTMLElement>(`[data-col="${status}"]`);
    if (!el || !col) return;
    const rtl = getComputedStyle(el).direction === "rtl";
    const a = col.getBoundingClientRect();
    const b = el.getBoundingClientRect();
    el.scrollBy({ left: rtl ? a.right - b.right : a.left - b.left, behavior: "smooth" });
  };

  /** dir +1 = towards the later columns (to the left in Persian). */
  const page = (dir: 1 | -1) => {
    const el = ref.current;
    if (!el) return;
    const rtl = getComputedStyle(el).direction === "rtl";
    el.scrollBy({ left: dir * (rtl ? -1 : 1) * el.clientWidth * 0.8, behavior: "smooth" });
  };

  // Mouse drag on the empty background pans the board. Cards (buttons, selects) keep their own behaviour.
  const onPointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    if (e.pointerType !== "mouse" || e.button !== 0) return;
    if ((e.target as HTMLElement).closest("button, select, a, input, [draggable='true']")) return;
    pan.current = { x: e.clientX, left: ref.current!.scrollLeft, moved: false };
  };
  const onPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    const p = pan.current;
    if (!p) return;
    const dx = e.clientX - p.x;
    if (!p.moved && Math.abs(dx) < 5) return;
    if (!p.moved) {
      p.moved = true;
      setPanning(true);
      ref.current!.setPointerCapture(e.pointerId);
    }
    ref.current!.scrollLeft = p.left - dx;
  };
  const endPan = (e: React.PointerEvent<HTMLDivElement>) => {
    if (pan.current?.moved && ref.current?.hasPointerCapture(e.pointerId)) ref.current.releasePointerCapture(e.pointerId);
    pan.current = null;
    setPanning(false);
  };

  const arrow = "grid size-10 shrink-0 place-items-center rounded-full border bg-card transition-opacity hover:bg-muted disabled:pointer-events-none disabled:opacity-30";

  return (
    <div className="space-y-3">
      <nav aria-label={t("work.jump")} className="flex items-center gap-2">
        {/* In Persian the later columns sit to the left: "previous" points right, "next" points left. */}
        <button type="button" className={arrow} onClick={() => page(-1)} disabled={!edges.start} aria-label={t("work.prev")} data-testid="board-prev">
          <ChevronRight className="size-5 ltr:rotate-180" aria-hidden="true" />
        </button>
        <ol className="flex min-w-0 flex-1 gap-1.5 overflow-x-auto [scrollbar-width:none]">
          {columns.map((s) => (
            <li key={s} className="shrink-0">
              <button
                type="button"
                onClick={() => jump(s)}
                aria-current={visible.has(s) ? "true" : undefined}
                className={cn(
                  "h-9 rounded-full border px-3 text-sm transition-colors",
                  visible.has(s) ? "border-foreground/30 bg-foreground/10 font-semibold text-foreground" : "bg-card text-muted-foreground hover:text-foreground",
                )}
                data-testid={`jump-${s}`}
              >
                {t(`work.col.${s}` as TextKey)} <span className="tabular-nums opacity-70">{formatNumber(counts[s] ?? 0)}</span>
              </button>
            </li>
          ))}
        </ol>
        <button type="button" className={arrow} onClick={() => page(1)} disabled={!edges.end} aria-label={t("work.next")} data-testid="board-next">
          <ChevronLeft className="size-5 ltr:rotate-180" aria-hidden="true" />
        </button>
      </nav>

      <div className="relative">
        <div
          ref={ref}
          onScroll={measure}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={endPan}
          onPointerCancel={endPan}
          title={t("work.panHint")}
          style={height ? { height } : undefined}
          className={cn(
            "kn-board flex h-[calc(100dvh-18rem)] min-h-96 gap-3 overflow-x-auto pb-3",
            panning ? "cursor-grabbing select-none" : "cursor-grab snap-x snap-proximity",
          )}
          data-testid="board-scroll"
        >
          {children}
        </div>
        {/* Soft edges where more columns are hidden. */}
        <div aria-hidden="true" className={cn("pointer-events-none absolute inset-y-0 start-0 w-10 bg-gradient-to-l from-transparent to-background transition-opacity rtl:bg-gradient-to-r", edges.start ? "opacity-100" : "opacity-0")} />
        <div aria-hidden="true" className={cn("pointer-events-none absolute inset-y-0 end-0 w-10 bg-gradient-to-r from-transparent to-background transition-opacity rtl:bg-gradient-to-l", edges.end ? "opacity-100" : "opacity-0")} />
      </div>
    </div>
  );
}
