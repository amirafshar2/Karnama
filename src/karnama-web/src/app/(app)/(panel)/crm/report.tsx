"use client";

import { useQuery } from "@/lib/query";
import { useState } from "react";
import { ErrorText } from "@/components/app/error-text";
import { JalaliDateField } from "@/components/app/form";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { CrmReport } from "./types";

const iso = (d: Date) => d.toISOString().slice(0, 10);

/** Calls per marketer, conversion per column, the best sources, sales (and commission when switched on) in a date range. */
export function Report({ seesAll }: { seesAll: boolean }) {
  const { t, formatNumber } = useI18n();
  const [from, setFrom] = useState<string | null>(() => iso(new Date(Date.now() - 30 * 24 * 3600_000)));
  const [to, setTo] = useState<string | null>(() => iso(new Date()));
  const report = useQuery({
    queryKey: ["crm-report", from, to],
    enabled: !!from && !!to,
    queryFn: () => api<CrmReport>(`/api/v1/crm/report?from=${from!.slice(0, 10)}&to=${to!.slice(0, 10)}`),
  });
  const r = report.data;
  const toman = (rial: number) => formatNumber(Math.round(rial / 10));
  const hasCommission = r?.marketers.some((m) => m.commission !== null) ?? false;
  return (
    <section className="space-y-6" data-testid="crm-report">
      <div className="flex flex-wrap items-end gap-3">
        <div className="w-44"><JalaliDateField label={t("crm.report.from")} help={t("crm.report.fromHelp")} value={from} onChange={setFrom} /></div>
        <div className="w-44"><JalaliDateField label={t("crm.report.to")} help={t("crm.report.toHelp")} value={to} onChange={setTo} /></div>
      </div>
      <ErrorText error={report.error} />
      {r && (
        <>
          <p className="text-lg font-bold">{t("crm.report.total", { count: formatNumber(r.totalLeads) })}</p>
          <div>
            <h2 className="mb-2 font-bold">{t("crm.report.marketers")}</h2>
            <div className="overflow-x-auto rounded-xl border">
              <table className="w-full min-w-[36rem] text-sm">
                <thead className="bg-muted/60 text-start">
                  <tr>
                    <th className="p-2 text-start">{t("crm.owner")}</th>
                    <th className="p-2">{t("crm.report.leads")}</th>
                    <th className="p-2">{t("crm.report.calls")}</th>
                    <th className="p-2">{t("crm.report.callMinutes")}</th>
                    <th className="p-2">{t("crm.report.won")}</th>
                    <th className="p-2">{t("crm.report.lost")}</th>
                    <th className="p-2">{t("crm.report.sales")}</th>
                    {hasCommission && <th className="p-2">{t("crm.report.commission")}</th>}
                  </tr>
                </thead>
                <tbody>
                  {r.marketers.map((m) => (
                    <tr key={m.id} className="border-t text-center">
                      <td className="p-2 text-start font-medium">{m.name}</td>
                      <td className="p-2">{formatNumber(m.leads)}</td>
                      <td className="p-2">{formatNumber(m.calls)}</td>
                      <td className="p-2">{formatNumber(m.callMinutes)}</td>
                      <td className="p-2 font-bold text-emerald-700 dark:text-emerald-400">{formatNumber(m.won)}</td>
                      <td className="p-2">{formatNumber(m.lost)}</td>
                      <td className="p-2">{toman(m.sales)}</td>
                      {hasCommission && <td className="p-2">{m.commission !== null ? toman(m.commission) : "—"}</td>}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {!seesAll && <p className="mt-1 text-xs text-muted-foreground">{t("crm.subtitle")}</p>}
          </div>
          <div className="grid gap-6 lg:grid-cols-2">
            <div>
              <h2 className="mb-2 font-bold">{t("crm.report.stages")}</h2>
              <ul className="space-y-2">
                {r.stages.map((s) => (
                  <li key={s.stageId}>
                    <div className="flex justify-between text-sm"><span>{s.name}</span><span className="text-muted-foreground">{formatNumber(s.reached)} — {formatNumber(s.percent)}٪</span></div>
                    <div className="h-2 overflow-hidden rounded-full bg-muted"><div className="h-full bg-primary" style={{ width: `${Math.min(100, s.percent)}%` }} /></div>
                  </li>
                ))}
              </ul>
            </div>
            <div>
              <h2 className="mb-2 font-bold">{t("crm.report.sources")}</h2>
              <ul className="divide-y rounded-xl border">
                {r.sources.map((s) => (
                  <li key={s.sourceId ?? "none"} className="flex items-center justify-between gap-2 p-2 text-sm">
                    <span className="font-medium">{s.name}</span>
                    <span className="text-muted-foreground">{formatNumber(s.leads)} {t("crm.report.leads")} — {t("crm.report.rate")} {formatNumber(s.rate)}٪</span>
                  </li>
                ))}
              </ul>
            </div>
          </div>
        </>
      )}
    </section>
  );
}
