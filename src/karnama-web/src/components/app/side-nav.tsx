"use client";

import { LogOut, PanelRightClose, PanelRightOpen } from "lucide-react";
import Link from "next/link";
import { useSyncExternalStore } from "react";
import { BrandMark } from "@/components/app/brand-mark";
import { GROUP_OF, GROUPS, type NavItem } from "@/components/app/nav";
import { TilePattern } from "@/components/app/tile-pattern";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { cn } from "@/lib/utils";

const KEY = "karnama.sidebar.collapsed";
const listeners = new Set<() => void>();
/** Below this width the full sidebar would squeeze the page (tables wrap a word per line), so it starts as icons. */
const WIDE = "(min-width: 1280px)";
function read() {
  let stored: string | null = null;
  try {
    stored = localStorage.getItem(KEY);
  } catch {
    /* storage unavailable */
  }
  // The person's own choice wins; otherwise it follows the screen width.
  if (stored === "1" || stored === "0") return stored === "1";
  return !window.matchMedia(WIDE).matches;
}
function subscribe(l: () => void) {
  listeners.add(l);
  const mq = window.matchMedia(WIDE);
  mq.addEventListener("change", l);
  return () => {
    listeners.delete(l);
    mq.removeEventListener("change", l);
  };
}
function write(v: boolean) {
  try {
    localStorage.setItem(KEY, v ? "1" : "0");
  } catch {
    /* storage unavailable */
  }
  listeners.forEach((l) => l());
}

/**
 * Desktop/tablet sidebar (md+), from 21st: @wensity/sidebar — a dark rail with sections, a lit active pill,
 * a user card at the foot, and a collapse to icons that is remembered per browser (until chosen, it is icons below 1280px).
 * Rebuilt without its dependencies (framer-motion, base-ui, tabler) to stay inside the panels' 180 KB budget:
 * the motion is CSS (width + label fade), the icons are the app's lucide set.
 */
export function SideNav({ items, isActive, onLogout, user }: { items: NavItem[]; isActive: (href: string) => boolean; onLogout: () => void; user: { name: string; role: string | null } }) {
  const { t } = useI18n();
  const collapsed = useSyncExternalStore(subscribe, read, () => false);
  const Toggle = collapsed ? PanelRightOpen : PanelRightClose;
  const sections = GROUPS.map((g) => ({ g, list: items.filter((i) => (GROUP_OF[i.href] ?? "admin") === g) })).filter((s) => s.list.length > 0);

  return (
    <aside
      className={cn(
        "kn-side hidden shrink-0 transition-[width] duration-300 ease-[cubic-bezier(0.22,1,0.36,1)] md:block",
        collapsed ? "w-[4.75rem]" : "w-[17rem]",
      )}
      data-collapsed={collapsed ? "" : undefined}
    >
      <div className="sticky top-0 flex h-dvh flex-col p-3">
        <div className="relative flex h-14 shrink-0 items-center overflow-hidden rounded-2xl px-3">
          <TilePattern className="absolute inset-0 opacity-80 [--tile-line:color-mix(in_srgb,var(--brand-gold)_12%,transparent)]" id="side-tile" />
          <Link href="/dashboard" className="relative text-white">
            <BrandMark compact={collapsed} className="text-base" />
          </Link>
        </div>

        <nav aria-label={t("nav.main")} className="kn-side__scroll mt-3 flex-1 overflow-y-auto overscroll-contain pe-1">
          {sections.map(({ g, list }) => (
            <div key={g} className="mb-4">
              <p className={cn("kn-side__label px-3 pb-1.5 text-[0.72rem] font-bold", collapsed && "sr-only")}>{t(`nav.group.${g}` as TextKey)}</p>
              <ul className="space-y-0.5">
                {list.map((item) => {
                  const active = isActive(item.href);
                  return (
                    <li key={item.href}>
                      <Link
                        href={item.href}
                        data-testid={`nav-${item.href.slice(1)}`}
                        aria-current={active ? "page" : undefined}
                        title={collapsed ? t(item.label) : undefined}
                        className="kn-side__item flex min-h-11 items-center gap-3 rounded-xl px-3 text-sm"
                      >
                        <item.icon className="size-[1.15rem] shrink-0" aria-hidden="true" />
                        <span className={cn("truncate", collapsed && "sr-only")}>{t(item.label)}</span>
                      </Link>
                    </li>
                  );
                })}
              </ul>
            </div>
          ))}
        </nav>

        <div className="mt-2 shrink-0 space-y-1 border-t border-white/10 pt-3">
          <div className={cn("flex items-center gap-3 rounded-2xl bg-white/[0.06] p-2", collapsed && "justify-center")}>
            <span aria-hidden="true" className="grid size-9 shrink-0 place-items-center rounded-full bg-[var(--brand-yellow)] font-black text-[var(--primary-foreground)]">
              {user.name.trim().charAt(0)}
            </span>
            <span className={cn("min-w-0 flex-1 leading-tight", collapsed && "sr-only")}>
              <span className="block truncate text-sm font-bold text-white">{user.name}</span>
              {user.role && <span className="block truncate text-xs text-white/55">{user.role}</span>}
            </span>
            <button type="button" onClick={onLogout} title={t("nav.logout")} aria-label={t("nav.logout")} className={cn("grid size-9 shrink-0 place-items-center rounded-xl text-white/70 hover:bg-white/10 hover:text-white", collapsed && "hidden")}>
              <LogOut className="size-[1.1rem] rtl:rotate-180" aria-hidden="true" />
            </button>
          </div>
          {collapsed && (
            <button type="button" onClick={onLogout} title={t("nav.logout")} aria-label={t("nav.logout")} className="kn-side__item flex min-h-11 w-full items-center justify-center rounded-xl">
              <LogOut className="size-[1.1rem] rtl:rotate-180" aria-hidden="true" />
            </button>
          )}
          <button
            type="button"
            onClick={() => write(!collapsed)}
            aria-expanded={!collapsed}
            aria-label={collapsed ? t("nav.expand") : t("nav.collapse")}
            className="flex min-h-10 w-full items-center justify-center rounded-xl text-white/50 hover:bg-white/10 hover:text-white"
          >
            <Toggle className="size-5 ltr:rotate-180" aria-hidden="true" />
          </button>
        </div>
      </div>
    </aside>
  );
}
