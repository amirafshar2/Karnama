"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Ellipsis } from "lucide-react";
import { useEffect, useState } from "react";
import { notify } from "@/lib/notify";
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
      <SideNav items={items} isActive={isActive} onLogout={() => void logout()} />

      <div className="flex min-w-0 flex-1 flex-col">
        <DemoBanner />
        <OfflineBar />
        <IosInstallHint />
        <header className="safe-top sticky top-0 z-10 flex h-14 items-center gap-2 border-b bg-background/90 px-4 backdrop-blur">
          <BrandMark className="text-base md:hidden" />
          <span className="ms-auto truncate text-sm text-muted-foreground">{me.displayName}</span>
        </header>
        <main className="mx-auto w-full max-w-6xl flex-1 px-4 pt-6 pb-28 md:px-8 md:pb-10">{children}</main>
      </div>

      {/* Mobile: bottom navigation (thumb reach, 48px+ targets); more than 5 sections → 4 + "More". */}
      <BottomNav items={items} isActive={isActive} />
    </div>
  );
}

function BottomNav({ items, isActive }: { items: ReturnType<typeof navFor>; isActive: (href: string) => boolean }) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const overflow = items.length > 5;
  const main = overflow ? items.slice(0, 4) : items;
  const rest = overflow ? items.slice(4) : [];
  const cls = (active: boolean) =>
    cn(
      "flex min-h-16 w-full flex-col items-center justify-center gap-1 px-1 text-center text-xs leading-tight",
      active
        ? "font-bold text-foreground [&>svg]:rounded-md [&>svg]:bg-primary [&>svg]:text-primary-foreground [&>svg]:ring-4 [&>svg]:ring-primary"
        : "text-muted-foreground",
    );

  return (
    <nav aria-label={t("nav.main")} className="safe-bottom fixed inset-x-0 bottom-0 z-20 border-t bg-card/95 backdrop-blur md:hidden">
      {open && (
        <ul className="mx-auto max-w-md border-b p-2" data-testid="bottom-more-panel">
          {rest.map((item) => (
            <li key={item.href}>
              <Link
                href={item.href}
                onClick={() => setOpen(false)}
                data-testid={`bottom-${item.href.slice(1)}`}
                aria-current={isActive(item.href) ? "page" : undefined}
                className="flex min-h-12 items-center gap-3 rounded-md px-3 text-sm hover:bg-muted aria-[current=page]:font-bold"
              >
                <item.icon className="size-5" aria-hidden="true" />
                {t(item.label)}
              </Link>
            </li>
          ))}
        </ul>
      )}
      <ul className="mx-auto flex max-w-md">
        {main.map((item) => (
          <li key={item.href} className="flex-1">
            <Link href={item.href} data-testid={`bottom-${item.href.slice(1)}`} aria-current={isActive(item.href) ? "page" : undefined} className={cls(isActive(item.href))}>
              <item.icon className="size-5" aria-hidden="true" />
              {t(item.label)}
            </Link>
          </li>
        ))}
        {overflow && (
          <li className="flex-1">
            <button type="button" aria-expanded={open} onClick={() => setOpen(!open)} className={cls(rest.some((i) => isActive(i.href)))} data-testid="bottom-more">
              <Ellipsis className="size-5" aria-hidden="true" />
              {t("common.more")}
            </button>
          </li>
        )}
      </ul>
    </nav>
  );
}
