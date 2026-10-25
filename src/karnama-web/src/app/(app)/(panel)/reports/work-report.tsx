"use client";

import { ChevronDown, Download, Printer } from "lucide-react";
import { Fragment, useState } from "react";
import { ErrorText } from "@/components/app/error-text";
import { StarsShown } from "@/components/app/stars";
import { Button } from "@/components/ui/button";
import { api, refreshSession } from "@/lib/api";
import { API_BASE } from "@/lib/config";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQuery } from "@/lib/query";
import { tokenStore } from "@/lib/token-store";
import { cn } from "@/lib/utils";
import { DayChart } from "./day-chart";
import type { WorkDetails, WorkReport, WorkRow } from "./types";

export function WorkReportView({ range, canExport }: { range: string; canExport: boolean }) {
  const { t, formatNumber } = useI18n();
  const rep = useQuery({ queryKey: ["work-report", range], queryFn: () => api<WorkReport>(`/api/v1/reports/work?${range}`) });
  const [open, setOpen] = useState<string | null>(null);
  const hm = (m: number) => `${formatNumber(Math.floor(m / 60))}:${formatNumber(m % 60).padStart(2, formatNumber(0))}`;
  const pct = (v: number | null) => (v === null ? "—" : `${formatNumber(Math.round(v))}٪`);

  const download = async () => {
    const get = () => fetch(`${API_BASE}/api/v1/reports/work/export?${range}`, { credentials: "include", headers: tokenStore.access ? { Authorization: `Bearer ${tokenStore.access}` } : undefined });
    let res = await get();
    if (res.status === 401 && (await refreshSession())) res = await get();
    if (!res.ok) return notify.error(t("common.errorGeneric"));
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement("a");
    a.href = url;
    a.download = `work-report-${range.replace(/[^0-9-]/g, "")}.xlsx`;
    a.click();
    URL.revokeObjectURL(url);
  };

  if (rep.error) return <ErrorText error={rep.error} />;
  if (!rep.data) return <div className="h-64 animate-pulse rounded-xl bg-muted" />;
  const r = rep.data;
  const cols: TextKey[] = ["rpt.name", "rpt.presence", "rpt.timer", "rpt.useful", "rpt.overtime", "rpt.late", "rpt.absent", "rpt.done", "rpt.onTime", "rpt.firstTime", "rpt.revisions", "rpt.score"];

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap gap-2" data-print-hide>
        {canExport && (
          <Button variant="outline" className="h-12 md:h-11" onClick={download} data-testid="rpt-excel">
            <Download aria-hidden="true" />
            {t("rpt.excel")}
          </Button>
        )}
        <Button variant="outline" className="h-12 md:h-11" onClick={() => window.print()} data-testid="rpt-print">
          <Printer aria-hidden="true" />
          {t("rpt.print")}
        </Button>
      </div>

      {r.days.length > 0 && <DayChart days={r.days} />}

      {r.rows.length === 0 ? (
        <p className="rounded-xl border border-dashed p-6 text-center text-muted-foreground">{t("rpt.empty")}</p>
      ) : (
        <div className="overflow-x-auto rounded-xl border bg-card" data-testid="rpt-table">
          <table className="w-full min-w-[960px] text-sm">
            <thead className="border-b bg-muted/40">
              <tr>
                {cols.map((k) => (
                  <th key={k} scope="col" className="h-11 px-3 text-start font-semibold whitespace-nowrap">
                    {t(k)}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y">
              {r.rows.map((row) => (
                <Fragment key={row.userId}>
                  <tr className={cn(open === row.userId && "bg-muted/30")}>
                    <th scope="row" className="px-3 py-2 text-start font-semibold">
                      <button type="button" className="flex min-h-11 items-center gap-1.5 text-start" aria-expanded={open === row.userId} onClick={() => setOpen(open === row.userId ? null : row.userId)} data-testid={`rpt-row-${row.name}`}>
                        <ChevronDown aria-hidden="true" className={cn("size-4 shrink-0 transition-transform", open === row.userId && "rotate-180")} />
                        {row.name}
                        <span className="sr-only">{open === row.userId ? t("rpt.hideDetails") : t("rpt.details", { name: row.name })}</span>
                      </button>
                    </th>
                    <td className="px-3 py-2 tabular-nums">{hm(row.presenceMinutes)}</td>
                    <td className="px-3 py-2 tabular-nums">{hm(row.timerMinutes)}</td>
                    <td className="px-3 py-2">{pct(row.usefulPercent)}</td>
                    <td className="px-3 py-2">{row.overtimeMinutes > 0 ? hm(row.overtimeMinutes) : "—"}</td>
                    <td className="px-3 py-2">{row.lateCount > 0 ? `${formatNumber(row.lateMinutes)}′ (${formatNumber(row.lateCount)})` : "—"}</td>
                    <td className="px-3 py-2">{row.unexcusedAbsences + row.excusedAbsences > 0 ? `${formatNumber(row.unexcusedAbsences)} / ${formatNumber(row.excusedAbsences)}` : "—"}</td>
                    <td className="px-3 py-2 font-semibold">{formatNumber(row.doneCount)}</td>
                    <td className="px-3 py-2">{pct(row.onTimePercent)}</td>
                    <td className="px-3 py-2">{pct(row.firstTimePercent)}</td>
                    <td className="px-3 py-2">{formatNumber(row.revisions)}</td>
                    <td className="px-3 py-2 font-bold">{row.score === null ? "—" : formatNumber(row.score)}</td>
                  </tr>
                  {open === row.userId && (
                    <tr>
                      <td colSpan={cols.length} className="bg-muted/20 p-4">
                        <Details row={row} range={range} />
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function Details({ row, range }: { row: WorkRow; range: string }) {
  const { t, formatNumber, formatDate } = useI18n();
  const d = useQuery({ queryKey: ["work-report-user", row.userId, range], queryFn: () => api<WorkDetails>(`/api/v1/reports/work/${row.userId}?${range}`) });
  const time = (s: string) => formatDate(s, { hour: "2-digit", minute: "2-digit" });
  const day = (s: string) => formatDate(s, { weekday: "short", day: "numeric", month: "short" });
  if (d.error) return <ErrorText error={d.error} />;
  if (!d.data) return <div className="h-24 animate-pulse rounded-lg bg-muted" />;
  return (
    <div className="grid gap-5 lg:grid-cols-2" data-testid="rpt-details">
      {row.doneByService.length > 0 && (
        <section className="lg:col-span-2">
          <h4 className="mb-2 font-bold">{t("rpt.byService")}</h4>
          <ul className="flex flex-wrap gap-2">
            {row.doneByService.map((s) => (
              <li key={s.serviceTypeId} className="rounded-full border bg-card px-3 py-1">
                {s.name}: <strong>{formatNumber(s.count)}</strong>
              </li>
            ))}
          </ul>
        </section>
      )}
      <section className="lg:col-span-2">
        <h4 className="mb-2 font-bold">{t("rpt.doneList")}</h4>
        <div className="overflow-x-auto rounded-lg border bg-card">
          <table className="w-full min-w-[720px] text-xs">
            <thead className="border-b bg-muted/40">
              <tr>
                {(["rpt.done", "rpt.estimated", "rpt.actual", "rpt.deadline", "rpt.delivered", "rpt.onTime", "rpt.revisions", "rpt.manager", "rpt.client"] as TextKey[]).map((k) => (
                  <th key={k} scope="col" className="h-9 px-2 text-start font-semibold">{t(k)}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y">
              {d.data.done.map((x) => (
                <tr key={x.id}>
                  <th scope="row" className="px-2 py-1.5 text-start font-medium">
                    {x.title} <span className="text-muted-foreground">#{formatNumber(x.orderNumber)} · {x.service}</span>
                  </th>
                  <td className="px-2 py-1.5">{formatNumber(x.estimated)}′</td>
                  <td className="px-2 py-1.5">{formatNumber(x.actual)}′</td>
                  <td className="px-2 py-1.5">{x.deadline ? `${day(x.deadline)} ${time(x.deadline)}` : "—"}</td>
                  <td className="px-2 py-1.5">{x.delivered ? `${day(x.delivered)} ${time(x.delivered)}` : "—"}</td>
                  <td className="px-2 py-1.5">{x.onTime === null ? "—" : x.onTime ? t("rpt.yes") : <span className="font-semibold text-destructive">{t("rpt.no")}</span>}</td>
                  <td className="px-2 py-1.5">{formatNumber(x.revisions)}</td>
                  <td className="px-2 py-1.5"><StarsShown value={x.managerStars} /></td>
                  <td className="px-2 py-1.5"><StarsShown value={x.clientStars} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
      <section>
        <h4 className="mb-2 font-bold">{t("rpt.shifts")}</h4>
        <ul className="max-h-72 divide-y overflow-y-auto rounded-lg border bg-card text-xs">
          {d.data.shifts.map((s) => (
            <li key={s.in} className="flex justify-between gap-2 px-3 py-1.5">
              <span>{day(s.in)}</span>
              <span className="tabular-nums">
                {time(s.in)} – {s.out ? time(s.out) : t("rpt.open")}
                {s.late > 0 && <span className="ms-2 text-destructive">+{formatNumber(s.late)}′</span>}
              </span>
            </li>
          ))}
        </ul>
      </section>
      <section>
        <h4 className="mb-2 font-bold">{t("rpt.timers")}</h4>
        <ul className="max-h-72 divide-y overflow-y-auto rounded-lg border bg-card text-xs">
          {d.data.timers.map((x) => (
            <li key={x.start} className="flex justify-between gap-2 px-3 py-1.5">
              <span className="truncate">{x.title}</span>
              <span className="shrink-0 tabular-nums">{day(x.start)} · {t("rpt.minutes", { n: formatNumber(x.minutes) })}</span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
