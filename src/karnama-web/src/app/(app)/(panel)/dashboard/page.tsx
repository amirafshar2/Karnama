"use client";

import { BarChart3, Globe, Smartphone } from "lucide-react";
import Link from "next/link";
import { navFor } from "@/components/app/nav";
import { TilePattern } from "@/components/app/tile-pattern";
import { useAuth } from "@/lib/auth";
import type { TextKey } from "@/lib/i18n/registry";
import { useI18n } from "@/lib/i18n/provider";
import { hasPermission, permissionGroup, type Scope } from "@/lib/types";
import dynamic from "next/dynamic";

const PortalHome = dynamic(() => import("@/components/portal/portal-home").then((m) => m.PortalHome), { ssr: false });
const OnboardingCard = dynamic(() => import("@/components/app/onboarding-card").then((m) => m.OnboardingCard), { ssr: false });

import { faAccess } from "@/lib/i18n/fa-access";
import { registerTexts } from "@/lib/i18n/registry";
import { faDashboard } from "@/lib/i18n/fa-pages";
import { registerTexts as registerPageTexts } from "@/lib/i18n/registry";

registerPageTexts(faDashboard);

registerTexts(faAccess);

const NEXT: { key: TextKey; icon: typeof Globe }[] = [
  { key: "dashboard.next.site", icon: Globe },
  { key: "dashboard.next.app", icon: Smartphone },
  { key: "dashboard.next.reports", icon: BarChart3 },
];

export default function DashboardPage() {
  const { me } = useAuth();
  const { t, formatDate } = useI18n();
  if (!me) return null;
  // Clients get their own simple panel (docs/PROMPT.md §6.9).
  if (me.kind === "client") return <PortalHome />;

  const shortcuts = navFor(me).filter((i) => i.href !== "/dashboard" && i.href !== "/help" && i.href !== "/profile").slice(0, 6);
  const groups = new Map<string, [string, Scope][]>();
  for (const [code, scope] of Object.entries(me.permissions)) {
    const g = permissionGroup(code);
    groups.set(g, [...(groups.get(g) ?? []), [code, scope]]);
  }

  return (
    <div className="space-y-8">
      {/* Greeting banner on the brand black with the tile lattice (21st: @scrollxui/hero-with-cards' stage, panel-sized). */}
      <section className="relative isolate overflow-hidden rounded-3xl bg-[var(--brand-black)] p-6 text-white sm:p-8">
        <TilePattern className="absolute inset-0 -z-10 opacity-70 [--tile-line:color-mix(in_srgb,var(--brand-gold)_10%,transparent)] [mask-image:linear-gradient(90deg,transparent,#000)]" id="dash-tile" />
        <div aria-hidden="true" className="absolute -top-24 -end-16 -z-10 size-72 rounded-full bg-[var(--brand-yellow)] opacity-25 blur-3xl" />
        <p className="text-sm text-white/60">{t("dashboard.today", { date: formatDate(new Date()) })}</p>
        <h1 className="mt-2 text-2xl font-black sm:text-4xl" data-testid="dashboard-hello">
          {t("dashboard.hello", { name: me.displayName })}
        </h1>
        {me.roles.length > 0 && (
          <p className="mt-3 inline-flex rounded-full bg-[var(--brand-yellow)] px-3 py-1 text-xs font-bold text-[var(--primary-foreground)]">{t("dashboard.rolesLine", { roles: me.roles.join("، ") })}</p>
        )}
        {shortcuts.length > 0 && (
          <ul className="mt-7 grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-6">
            {shortcuts.map((i) => (
              <li key={i.href}>
                <Link href={i.href} className="group flex h-full flex-col gap-3 rounded-2xl bg-white/[0.07] p-4 transition-colors hover:bg-[var(--brand-yellow)] hover:text-[var(--primary-foreground)]">
                  <i.icon className="size-5 transition-transform group-hover:-rotate-6" aria-hidden="true" />
                  <span className="text-sm font-bold">{t(i.label)}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </section>

      {hasPermission(me, "settings.manage", "all") && <OnboardingCard />}

      <section className="rounded-3xl border bg-card p-5 sm:p-7" aria-labelledby="access-title">
          <h2 id="access-title" className="text-lg font-bold">
            {t("dashboard.accessTitle")}
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">{t("dashboard.accessHint")}</p>
          <dl className="mt-5 grid gap-x-8 gap-y-5 sm:grid-cols-2 lg:grid-cols-3">
            {[...groups.entries()].map(([group, perms]) => (
              <div key={group}>
                <dt className="text-sm font-bold text-emphasis">{t(`permGroup.${group}` as TextKey)}</dt>
                <dd>
                  <ul className="mt-2 space-y-1.5 text-sm">
                    {perms.map(([code, scope]) => (
                      <li key={code} className="flex flex-wrap items-baseline justify-between gap-x-2 border-b border-dashed pb-1.5 last:border-0">
                        <span>{t(`perm.${code}` as TextKey)}</span>
                        <span className="text-xs text-muted-foreground">{t(`scope.${scope}` as TextKey)}</span>
                      </li>
                    ))}
                  </ul>
                </dd>
              </div>
            ))}
          </dl>
        </section>

      <section aria-labelledby="next-title">
        <h2 id="next-title" className="text-lg font-bold">
          {t("dashboard.nextTitle")}
        </h2>
        <ul className="mt-3 grid gap-3 sm:grid-cols-2">
          {NEXT.map(({ key, icon: Icon }) => (
            <li key={key} className="flex items-center gap-3 rounded-2xl border border-dashed px-4 py-4 text-muted-foreground">
              <Icon className="size-5 shrink-0" aria-hidden="true" />
              {t(key)}
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
