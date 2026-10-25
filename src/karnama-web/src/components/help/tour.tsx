"use client";

import { usePathname } from "next/navigation";
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { navFor } from "@/components/app/nav";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/lib/auth";
import { faN12 } from "@/lib/i18n/fa-n12";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { hasPermission, type Me } from "@/lib/types";
import { useBrand } from "@/lib/use-brand";

registerTexts(faN12);

// ⚠ TEMPORARY STAND-IN: no 21st.dev "product tour" yet (docs/21st-components.md). Spotlight + card, keyboard and screen-reader friendly.

/** Anyone can ask for the tour again (help page button). */
export const TOUR_EVENT = "karnama:tour";
export const startTour = () => window.dispatchEvent(new Event(TOUR_EVENT));

type Step = { target?: string; nav?: string; title: TextKey; body: TextKey };

function stepsFor(me: Me): Step[] {
  const has = (href: string) => navFor(me).some((i) => i.href === href);
  const nav = (href: string, title: TextKey, body: TextKey): Step[] => (has(href) ? [{ nav: href, title, body }] : []);
  const end: Step[] = [{ target: "[data-testid=bell]", title: "tour.bellTitle", body: "tour.bellBody" }, { target: "[data-tour=help-button]", title: "tour.helpTitle", body: "tour.helpBody" }];
  if (me.kind === "client")
    return [
      { title: "tour.welcomeTitle", body: "tour.clientWelcomeBody" },
      { target: "[data-tour=approvals]", title: "tour.approvalsTitle", body: "tour.approvalsBody" },
      { target: "[data-tour=package]", title: "tour.packageTitle", body: "tour.packageBody" },
      ...nav("/chat", "tour.chatTitle", "tour.chatBody"),
      end[1],
    ];
  const welcome: Step = { title: "tour.welcomeTitle", body: "tour.welcomeBody" };
  if (hasPermission(me, "work.assign", "all"))
    return [welcome, ...nav("/orders", "tour.ordersTitle", "tour.ordersBody"), ...nav("/work", "tour.workTitle", "tour.workBody"),
      ...nav("/clients", "tour.clientsTitle", "tour.clientsBody"), ...nav("/reports", "tour.reportsTitle", "tour.reportsBody"), ...end];
  if (hasPermission(me, "crm.leads", "own") && !hasPermission(me, "work.view", "assigned"))
    return [welcome, ...nav("/crm", "tour.crmTitle", "tour.crmBody"), ...nav("/attendance", "tour.attendanceTitle", "tour.attendanceBody"),
      ...nav("/chat", "tour.chatTitle", "tour.chatBody"), ...end];
  return [welcome, ...nav("/work", "tour.workTitle", "tour.workBody"), ...nav("/attendance", "tour.attendanceTitle", "tour.attendanceBody"),
    ...nav("/chat", "tour.chatTitle", "tour.chatBody"), ...end];
}

const seenKey = (me: Me) => `karnama.tour.v1.${me.id}`;
const visible = (el: Element | null): el is HTMLElement => !!el && (el as HTMLElement).getClientRects().length > 0 && (el as HTMLElement).getBoundingClientRect().width > 0;

function findTarget(s: Step): HTMLElement | null {
  if (s.nav) {
    const slug = s.nav.slice(1);
    const side = document.querySelector(`[data-testid="nav-${slug}"]`);
    if (visible(side)) return side;
    const bottom = document.querySelector(`[data-testid="bottom-${slug}"]`);
    return visible(bottom) ? bottom : null;
  }
  if (s.target) {
    const el = document.querySelector(s.target);
    return visible(el) ? el : null;
  }
  return null;
}

/** Shown once per person on the first visit to the dashboard; again whenever they ask from «راهنما». */
export function TourHost() {
  const { me } = useAuth();
  const path = usePathname();
  const [steps, setSteps] = useState<Step[] | null>(null);

  const open = useCallback(() => me && setSteps(stepsFor(me)), [me]);
  useEffect(() => {
    window.addEventListener(TOUR_EVENT, open);
    return () => window.removeEventListener(TOUR_EVENT, open);
  }, [open]);
  useEffect(() => {
    if (!me || path !== "/dashboard") return;
    let seen = true;
    try {
      // "karnama.tour.off": automated tests (and anyone who never wants it) switch the automatic start off.
      seen = localStorage.getItem(seenKey(me)) === "1" || localStorage.getItem("karnama.tour.off") === "1";
    } catch {
      /* private mode: don't nag */
    }
    if (seen) return;
    const id = window.setTimeout(open, 900);
    return () => window.clearTimeout(id);
  }, [me, path, open]);

  if (!steps || !me) return null;
  const close = () => {
    try {
      localStorage.setItem(seenKey(me), "1");
    } catch {
      /* ignore */
    }
    setSteps(null);
  };
  return <Tour steps={steps} onClose={close} />;
}

function Tour({ steps, onClose }: { steps: Step[]; onClose: () => void }) {
  const { t, formatNumber } = useI18n();
  const brand = useBrand();
  const [i, setI] = useState(0);
  const [rect, setRect] = useState<DOMRect | null>(null);
  const card = useRef<HTMLDivElement>(null);
  const step = steps[i];
  const last = i === steps.length - 1;

  useLayoutEffect(() => {
    const measure = () => {
      const el = findTarget(step);
      if (el) el.scrollIntoView({ block: "nearest", inline: "nearest" });
      setRect(el ? el.getBoundingClientRect() : null);
    };
    measure();
    window.addEventListener("resize", measure);
    window.addEventListener("scroll", measure, true);
    return () => {
      window.removeEventListener("resize", measure);
      window.removeEventListener("scroll", measure, true);
    };
  }, [step]);
  useEffect(() => card.current?.focus(), [i]);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
      if (e.key === "ArrowLeft" || e.key === "ArrowRight") {
        const forward = (e.key === "ArrowLeft") === (document.documentElement.dir === "rtl");
        setI((x) => Math.max(0, Math.min(steps.length - 1, x + (forward ? 1 : -1))));
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose, steps.length]);

  // Card goes below the target, or above it when the target sits in the lower half (bottom nav on phones).
  const pad = 6;
  const below = rect ? rect.top + rect.height / 2 < window.innerHeight / 2 : false;
  const cardStyle: React.CSSProperties = rect
    ? below ? { top: Math.min(rect.bottom + 14, window.innerHeight - 220) } : { bottom: Math.max(window.innerHeight - rect.top + 14, 16) }
    : { top: "50%", transform: "translateY(-50%)" };

  return (
    <div className="fixed inset-0 z-[70]" data-testid="tour">
      {rect ? (
        <div aria-hidden="true" className="pointer-events-none fixed rounded-xl ring-4 ring-primary"
          style={{ top: rect.top - pad, left: rect.left - pad, width: rect.width + pad * 2, height: rect.height + pad * 2, boxShadow: "0 0 0 9999px rgb(0 0 0 / 0.55)" }} />
      ) : (
        <div aria-hidden="true" className="fixed inset-0 bg-black/55" />
      )}
      <div ref={card} role="dialog" aria-modal="true" aria-labelledby="tour-title" aria-describedby="tour-body" tabIndex={-1}
        className="fixed inset-x-4 mx-auto max-w-sm rounded-2xl bg-card p-5 text-card-foreground shadow-xl outline-none" style={cardStyle}>
        <p className="text-xs text-muted-foreground">{t("tour.step", { n: formatNumber(i + 1), total: formatNumber(steps.length) })}</p>
        <h2 id="tour-title" className="mt-1 text-lg font-extrabold">{t(step.title, { brand: brand.name ?? t("brand.defaultName") })}</h2>
        <p id="tour-body" className="mt-2 leading-7">{t(step.body)}</p>
        <div className="mt-4 flex items-center gap-2">
          <Button variant="ghost" className="h-11" onClick={onClose} data-testid="tour-skip">{t("tour.skip")}</Button>
          <span className="flex-1" />
          {i > 0 && <Button variant="outline" className="h-11" onClick={() => setI(i - 1)}>{t("tour.prev")}</Button>}
          <Button className="h-11" onClick={() => (last ? onClose() : setI(i + 1))} data-testid="tour-next">{last ? t("tour.done") : t("tour.next")}</Button>
        </div>
      </div>
    </div>
  );
}
