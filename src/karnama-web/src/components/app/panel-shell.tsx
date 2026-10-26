"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Ellipsis } from "lucide-react";
import { useEffect, useState } from "react";
import { notify } from "@/lib/notify";
import { BellButton } from "@/components/app/bell";
import { BrandMark } from "@/components/app/brand-mark";
import { DemoBanner } from "@/components/app/demo-banner";
import { navFor } from "@/components/app/nav";
import { OfflineBar } from "@/components/app/offline-bar";
import dynamic from "next/dynamic";
import { Skeleton } from "@/components/ui/skeleton";
import { SideNav } from "@/components/app/side-nav";
import { refreshSession } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n/provider";
import { connectRealtime } from "@/lib/realtime";
import { cn } from "@/lib/utils";

// Only iPhone/iPad Safari users ever see it, so it is not part of the first download.
const IosInstallHint = dynamic(() => import("@/components/app/pwa").then((m) => m.IosInstallHint), { ssr: false });
// Help ("؟" + first-run tour) loads after the page itself, so it never slows the first screen.
const PageHelp = dynamic(() => import("@/components/help/page-help").then((m) => m.PageHelp), { ssr: false, loading: () => <span className="size-11" /> });
const TourHost = dynamic(() => import("@/components/help/tour").then((m) => m.TourHost), { ssr: false });

export function PanelShell({ children }: { children: React.ReactNode }) {
  const { status, me, logout, reloadMe } = useAuth();
  const { t } = useI18n();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (status === "anonymous") router.replace("/login");
  }, [status, router]);

  // Live: role changes arrive over SignalR -> fresh token + menu without a reload.
  useEffect(() => {
    if (status !== "authenticated") return;
    let dispose: (() => void) | undefined;
    let cancelled = false;
    connectRealtime({
      permissionsChanged: async () => {
        if (await refreshSession()) await reloadMe();
        notify.info(t("realtime.permissionsChanged"));
      },
    }).then((d) => (cancelled ? d() : (dispose = d)));
    return () => {
      cancelled = true;
      dispose?.();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status]);

  if (status !== "authenticated" || !me) {
    return (
      <div className="space-y-4 p-6" aria-busy="true" aria-label={t("common.loading")}>
        <Skeleton className="h-10 w-48" />
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-32 w-full" />
      </div>
    );
  }

  const items = navFor(me);
  // Only the most specific menu entry is active (/attendance/live must not also light up /attendance).
  const best = items.filter((i) => pathname === i.href || pathname?.startsWith(i.href + "/")).sort((a, b) => b.href.length - a.href.length)[0]?.href;
  const isActive = (href: string) => href === best;

  return (
    <div className="flex min-h-dvh">
      <SideNav items={items} isActive={isActive} onLogout={() => void logout()} user={{ name: me.displayName, role: me.roles[0] ?? null }} />

      <div className="flex min-w-0 flex-1 flex-col">
        <DemoBanner />
        <OfflineBar />
        <IosInstallHint />
        <header className="kn-top safe-top sticky top-0 z-10 flex h-16 items-center gap-2 px-4 md:px-8">
          <BrandMark className="text-base md:hidden" />
          <span className="ms-auto hidden truncate text-sm text-muted-foreground sm:inline">{me.displayName}</span>
          <PageHelp />
          <BellButton />
          <span aria-hidden="true" className="grid size-9 shrink-0 place-items-center rounded-full bg-[var(--brand-black)] text-sm font-black text-[var(--brand-on-dark)] ring-2 ring-[var(--brand-yellow)]/60 md:hidden">
            {me.displayName.trim().charAt(0)}
          </span>
        </header>
        <main className="kn-main mx-auto w-full max-w-6xl flex-1 px-4 pt-6 pb-32 md:px-8 md:pb-12">{children}</main>
      </div>

      {/* Mobile: bottom navigation (thumb reach, 48px+ targets); more than 5 sections → 4 + "More". */}
      <BottomNav items={items} isActive={isActive} />
      <TourHost />
    </div>
  );
}

/**
 * Mobile / Android bottom bar, from 21st: @lyanchouss/dock — a floating dark capsule; the active section grows into a
 * lit pill with its name, the rest stay icons (48px+ targets). More than 5 sections → 4 + "More" sheet.
 */
function BottomNav({ items, isActive }: { items: ReturnType<typeof navFor>; isActive: (href: string) => boolean }) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const overflow = items.length > 5;
  const main = overflow ? items.slice(0, 4) : items;
  const rest = overflow ? items.slice(4) : [];
  const cls = (active: boolean) =>
    cn(
      "kn-dock__item flex h-14 min-w-12 items-center justify-center gap-2 rounded-full px-3 text-xs font-bold leading-tight",
      active && "kn-dock__item--on",
    );

  return (
    <nav aria-label={t("nav.main")} className="safe-bottom fixed inset-x-0 bottom-0 z-20 px-3 pb-3 md:hidden">
      {open && (
        <ul className="kn-dock__sheet mx-auto mb-2 max-h-[60dvh] max-w-md overflow-y-auto overscroll-contain rounded-3xl p-2" data-testid="bottom-more-panel">
          {rest.map((item) => (
            <li key={item.href}>
              <Link
                href={item.href}
                onClick={() => setOpen(false)}
                data-testid={`bottom-${item.href.slice(1)}`}
                aria-current={isActive(item.href) ? "page" : undefined}
                className="flex min-h-12 items-center gap-3 rounded-2xl px-3 text-sm text-white/85 hover:bg-white/10 aria-[current=page]:bg-[var(--brand-yellow)] aria-[current=page]:font-bold aria-[current=page]:text-[var(--primary-foreground)]"
              >
                <item.icon className="size-5" aria-hidden="true" />
                {t(item.label)}
              </Link>
            </li>
          ))}
        </ul>
      )}
      <ul className="kn-dock mx-auto flex max-w-md items-center justify-between gap-1 rounded-full p-1.5">
        {main.map((item) => {
          const on = isActive(item.href);
          return (
            <li key={item.href} className={on ? "flex-[1.6]" : "flex-1"}>
              <Link href={item.href} data-testid={`bottom-${item.href.slice(1)}`} aria-current={on ? "page" : undefined} aria-label={t(item.label)} className={cls(on)}>
                <item.icon className="size-5 shrink-0" aria-hidden="true" />
                <span className={on ? "truncate" : "sr-only"}>{t(item.label)}</span>
              </Link>
            </li>
          );
        })}
        {overflow && (
          <li className="flex-1">
            <button type="button" aria-expanded={open} onClick={() => setOpen(!open)} className={cls(open || rest.some((i) => isActive(i.href)))} data-testid="bottom-more" aria-label={t("common.more")}>
              <Ellipsis className="size-5" aria-hidden="true" />
              <span className="sr-only">{t("common.more")}</span>
            </button>
          </li>
        )}
      </ul>
    </nav>
  );
}
