"use client";

import { CalendarClock, Hourglass, MonitorSmartphone } from "lucide-react";
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

const NEXT: { key: TextKey; icon: typeof Hourglass }[] = [
  { key: "dashboard.next.time", icon: Hourglass },
  { key: "dashboard.next.leave", icon: CalendarClock },
  { key: "dashboard.next.portal", icon: MonitorSmartphone },
];

export default function DashboardPage() {
  const { me } = useAuth();
  const { t, formatDate } = useI18n();
  if (!me) return null;
  // Clients get their own simple panel (docs/PROMPT.md §6.9).
  if (me.kind === "client") return <PortalHome />;

  const groups = new Map<string, [string, Scope][]>();
  for (const [code, scope] of Object.entries(me.permissions)) {
    const g = permissionGroup(code);
    groups.set(g, [...(groups.get(g) ?? []), [code, scope]]);
  }

  return (
    <div className="space-y-8">
      <section>
        <h1 className="text-2xl font-extrabold sm:text-3xl" data-testid="dashboard-hello">
          {t("dashboard.hello", { name: me.displayName })}
        </h1>
        <p className="mt-1 text-muted-foreground">{t("dashboard.today", { date: formatDate(new Date()) })}</p>
        {me.roles.length > 0 && <p className="mt-1 text-muted-foreground">{t("dashboard.rolesLine", { roles: me.roles.join("، ") })}</p>}
      </section>

      {hasPermission(me, "settings.manage", "all") && <OnboardingCard />}

      <section className="rounded-xl border bg-card p-5 sm:p-6" aria-labelledby="access-title">
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
            <li key={key} className="flex items-center gap-3 rounded-lg border border-dashed px-4 py-3 text-muted-foreground">
              <Icon className="size-5 shrink-0" aria-hidden="true" />
              {t(key)}
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
