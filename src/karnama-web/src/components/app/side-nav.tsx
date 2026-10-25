"use client";

import { LogOut, PanelRightClose, PanelRightOpen } from "lucide-react";
import Link from "next/link";
import { useSyncExternalStore } from "react";
import { BrandMark } from "@/components/app/brand-mark";
import type { NavItem } from "@/components/app/nav";
import { TilePattern } from "@/components/app/tile-pattern";
import { useI18n } from "@/lib/i18n/provider";
import { cn } from "@/lib/utils";

const KEY = "karnama.sidebar.collapsed";
const listeners = new Set<() => void>();
function read() {
  try {
    return localStorage.getItem(KEY) === "1";
  } catch {
    return false;
  }
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
 * Desktop/tablet sidebar (md+). Collapses to icons; the choice is remembered per browser.
 * Mobile uses the bottom navigation instead.
 * ⚠ TEMPORARY STAND-IN for a 21st.dev sidebar (see docs/21st-components.md) — kept tiny for the bandwidth budget.
 */
export function SideNav({ items, isActive, onLogout }: { items: NavItem[]; isActive: (href: string) => boolean; onLogout: () => void }) {
  const { t } = useI18n();
  const collapsed = useSyncExternalStore(
    (l) => (listeners.add(l), () => listeners.delete(l)),
    read,
    () => false,
  );
  const Toggle = collapsed ? PanelRightOpen : PanelRightClose;

  return (
    <aside
      className={cn(
        "hidden shrink-0 border-e bg-sidebar text-sidebar-foreground transition-[width] duration-200 md:block",
        collapsed ? "w-[4.25rem]" : "w-64",
      )}
    >
      <div className="sticky top-0 flex h-dvh flex-col">
      <div className="relative flex h-14 items-center overflow-hidden border-b px-3">
        <TilePattern className="absolute inset-0 opacity-70" id="side-tile" />
        <Link href="/dashboard" className="relative">
          <BrandMark compact={collapsed} className="text-base" />
        </Link>
      </div>

      <nav aria-label={t("nav.main")} className="flex-1 overflow-y-auto p-2">
        <ul className="space-y-1">
          {items.map((item) => {
            const active = isActive(item.href);
            return (
              <li key={item.href}>
                <Link
                  href={item.href}
                  data-testid={`nav-${item.href.slice(1)}`}
                  aria-current={active ? "page" : undefined}
                  title={collapsed ? t(item.label) : undefined}
                  className={cn(
                    "flex min-h-11 items-center gap-3 rounded-md px-3 text-sm transition-colors",
                    active ? "bg-sidebar-accent font-bold text-sidebar-accent-foreground" : "hover:bg-muted",
                  )}
                >
                  <item.icon className="size-5 shrink-0" aria-hidden="true" />
                  <span className={cn("truncate", collapsed && "sr-only")}>{t(item.label)}</span>
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>

      <div className="space-y-1 border-t p-2">
        <button
          type="button"
          onClick={onLogout}
          title={collapsed ? t("nav.logout") : undefined}
          className="flex min-h-11 w-full items-center gap-3 rounded-md px-3 text-sm hover:bg-muted"
        >
          <LogOut className="size-5 shrink-0 rtl:rotate-180" aria-hidden="true" />
          <span className={cn(collapsed && "sr-only")}>{t("nav.logout")}</span>
        </button>
        <button
          type="button"
          onClick={() => write(!collapsed)}
          aria-expanded={!collapsed}
          aria-label={collapsed ? "»" : "«"}
          className="flex min-h-11 w-full items-center justify-center rounded-md text-muted-foreground hover:bg-muted"
        >
          <Toggle className="size-5 ltr:rotate-180" aria-hidden="true" />
        </button>
      </div>
      </div>
    </aside>
  );
}
