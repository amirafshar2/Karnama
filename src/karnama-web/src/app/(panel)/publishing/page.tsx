"use client";

import { ChevronLeft, ChevronRight, ExternalLink, Plus } from "lucide-react";
import dynamic from "next/dynamic";
import { useMemo, useState } from "react";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faM6 } from "@/lib/i18n/fa-m6";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { useQuery, useQueryClient } from "@/lib/query";
import type { KeysetPage } from "@/lib/types";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";
import { atTehran, splitTehran, statusTone, type PublishJob } from "@/components/publishing/types";

registerTexts(faM6);

const PublishEditor = dynamic(() => import("@/components/publishing/publish-editor").then((m) => m.PublishEditor), { ssr: false });

/** Saturday of the Tehran week that contains `day` (yyyy-mm-dd). */
function weekStart(day: string) {
  const d = new Date(day + "T12:00:00Z");
  const back = (d.getUTCDay() + 1) % 7; // Sat=0 … Fri=6
  d.setUTCDate(d.getUTCDate() - back);
  return d.toISOString().slice(0, 10);
}
const addDays = (day: string, n: number) => {
  const d = new Date(day + "T12:00:00Z");
  d.setUTCDate(d.getUTCDate() + n);
  return d.toISOString().slice(0, 10);
};

export default function PublishingPage() {
  const allowed = useRequire([{ code: "orders.manage", scope: "all" }]);
  const { t, formatDate, formatNumber } = useI18n();
  const qc = useQueryClient();
  const today = splitTehran(new Date().toISOString()).day;
  const [start, setStart] = useState(() => weekStart(today));
  const [clientId, setClientId] = useState("");
  const [editing, setEditing] = useState<{ job?: PublishJob; day?: string } | null>(null);
  const days = useMemo(() => Array.from({ length: 7 }, (_, i) => addDays(start, i)), [start]);
  const key = ["publishing", start, clientId];
  const jobs = useQuery({
    queryKey: key,
    enabled: allowed,
    queryFn: () =>
      api<PublishJob[]>(`/api/v1/publishing?from=${encodeURIComponent(atTehran(start, "00:00"))}&to=${encodeURIComponent(atTehran(addDays(start, 7), "00:00"))}${clientId ? `&clientId=${clientId}` : ""}`),
  });
  const clients = useQuery({ queryKey: ["clients", "options"], enabled: allowed, queryFn: () => api<KeysetPage<{ id: string; businessName: string }>>("/api/v1/clients?limit=100&status=active") });
  useLive(["publish"], [["publishing"]]);
  if (!allowed) return null;

  const byDay = (d: string) => (jobs.data ?? []).filter((j) => splitTehran(j.publishAt).day === d);
  const saved = async () => {
    setEditing(null);
    await qc.invalidateQueries({ queryKey: ["publishing"] });
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="max-w-2xl">
          <h1 className="text-2xl font-extrabold">{t("publish.title")}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t("publish.subtitle")}</p>
        </div>
        <Button className="h-12" onClick={() => setEditing({ day: today })} data-testid="publish-new">
          <Plus aria-hidden="true" />
          {t("publish.new")}
        </Button>
      </header>

      <div className="flex flex-wrap items-center gap-2">
        <Button variant="outline" size="icon" className="size-11" onClick={() => setStart(addDays(start, -7))} aria-label={t("publish.prevWeek")}>
          <ChevronRight className="ltr:rotate-180" aria-hidden="true" />
        </Button>
        <Button variant="outline" className="h-11" onClick={() => setStart(weekStart(today))} disabled={start === weekStart(today)}>
          {t("publish.thisWeek")}
        </Button>
        <Button variant="outline" size="icon" className="size-11" onClick={() => setStart(addDays(start, 7))} aria-label={t("publish.nextWeek")} data-testid="publish-next-week">
          <ChevronLeft className="ltr:rotate-180" aria-hidden="true" />
        </Button>
        <span className="text-sm font-semibold">
          {formatDate(start + "T12:00:00", { day: "numeric", month: "long" })} – {formatDate(addDays(start, 6) + "T12:00:00", { day: "numeric", month: "long", year: "numeric" })}
        </span>
        <select
          aria-label={t("publish.client")}
          className="ms-auto h-11 min-w-48 rounded-md border bg-transparent px-3 text-base"
          value={clientId}
          onChange={(e) => setClientId(e.target.value)}
          data-testid="publish-filter"
        >
          <option value="">{t("publish.allClients")}</option>
          {(clients.data?.items ?? []).map((c) => (
            <option key={c.id} value={c.id}>
              {c.businessName}
            </option>
          ))}
        </select>
      </div>

      {/* Phones: one day under another. Desktop: the week side by side. */}
      <ol className="grid gap-3 lg:grid-cols-7" aria-busy={jobs.isLoading}>
        {days.map((d) => {
          const list = byDay(d);
          return (
            <li key={d} className={cn("flex min-h-28 flex-col rounded-xl border bg-muted/30 p-2", d === today && "border-primary ring-1 ring-primary")} data-testid={`pub-day-${d}`}>
              <div className="mb-2 flex items-baseline justify-between px-1">
                <span className="text-sm font-bold">{formatDate(d + "T12:00:00", { weekday: "long" })}</span>
                <span className="text-xs text-muted-foreground">{formatDate(d + "T12:00:00", { day: "numeric", month: "short" })}</span>
              </div>
              <ul className="flex-1 space-y-2">
                {list.map((j) => (
                  <li key={j.id}>
                    <button
                      type="button"
                      onClick={() => setEditing({ job: j })}
                      className={cn("w-full rounded-lg border-2 p-2 text-start text-sm hover:shadow-sm", statusTone[j.status])}
                      data-testid="pub-card"
                    >
                      <span className="flex items-center justify-between gap-1">
                        <span className="font-bold tabular-nums" dir="ltr">
                          {splitTehran(j.publishAt).time.replace(/\d/g, (c) => formatNumber(Number(c)))}
                        </span>
                        <span className="text-xs">{t(`publish.status.${j.status}` as TextKey)}</span>
                      </span>
                      <span className="mt-1 block truncate font-semibold">{j.clientName}</span>
                      <span className="block truncate text-xs text-muted-foreground" dir="auto">
                        {j.platformName} @{j.handle}
                        {j.management === "clientManaged" && ` · ${t("publish.clientManaged")}`}
                      </span>
                      {j.caption && <span className="mt-1 line-clamp-2 text-xs">{j.caption}</span>}
                      {j.postUrl && (
                        <span className="mt-1 inline-flex items-center gap-1 text-xs font-semibold underline">
                          <ExternalLink className="size-3" aria-hidden="true" />
                          {t("publish.viewPost")}
                        </span>
                      )}
                    </button>
                  </li>
                ))}
              </ul>
              {list.length === 0 && <p className="px-1 pb-1 text-xs text-muted-foreground lg:hidden">{t("publish.empty")}</p>}
              <button
                type="button"
                className="mt-2 flex min-h-10 items-center justify-center gap-1 rounded-md text-xs text-muted-foreground hover:bg-muted hover:text-foreground"
                onClick={() => setEditing({ day: d })}
                aria-label={`${t("publish.new")} — ${formatDate(d + "T12:00:00", { weekday: "long", day: "numeric", month: "long" })}`}
              >
                <Plus className="size-3.5" aria-hidden="true" />
              </button>
            </li>
          );
        })}
      </ol>

      {editing && <PublishEditor job={editing.job} initial={{ day: editing.day, clientId: clientId || undefined }} onClose={() => setEditing(null)} onSaved={saved} />}
    </div>
  );
}
