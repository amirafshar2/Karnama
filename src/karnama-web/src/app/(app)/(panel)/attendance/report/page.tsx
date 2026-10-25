"use client";

import { useQuery } from "@/lib/query";
import { Download } from "lucide-react";
import { useState } from "react";
import { JalaliDateField } from "@/components/app/form";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api, refreshSession } from "@/lib/api";
import { API_BASE } from "@/lib/config";
import { faM5 } from "@/lib/i18n/fa-m5";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { tokenStore } from "@/lib/token-store";
import { toGregorian, toJalali } from "@/lib/jalali";

registerTexts(faM5);

type Row = { userId: string; name: string; workedMinutes: number; lateMinutes: number; lateCount: number; earlyLeaveMinutes: number; paidLeaveMinutes: number; unpaidLeaveMinutes: number; excusedAbsences: number; unexcusedAbsences: number; shifts: number };

export default function AttendanceReportPage() {
  const allowed = useRequire([{ code: "attendance.view", scope: "all" }]);
  const { t, formatNumber } = useI18n();
  const today = new Date();
  // Default: from the 1st of the current Persian month.
  const j = toJalali(today.getFullYear(), today.getMonth() + 1, today.getDate());
  const g = toGregorian(j.jy, j.jm, 1);
  const [from, setFrom] = useState<string | null>(`${g.gy}-${String(g.gm).padStart(2, "0")}-${String(g.gd).padStart(2, "0")}T12:00:00+03:30`);
  const [to, setTo] = useState<string | null>(today.toISOString());
  const range = from && to ? `from=${from.slice(0, 10)}&to=${to.slice(0, 10)}` : null;
  const rep = useQuery({ queryKey: ["att-report", range], enabled: allowed && !!range, queryFn: () => api<Row[]>(`/api/v1/leave/report?${range}`) });
  if (!allowed) return null;
  const hm = (m: number) => `${formatNumber(Math.floor(m / 60))}:${formatNumber(m % 60).padStart(2, formatNumber(0))}`;
  const download = async () => {
    const get = () => fetch(`${API_BASE}/api/v1/leave/report/export?${range}`, { credentials: "include", headers: tokenStore.access ? { Authorization: `Bearer ${tokenStore.access}` } : undefined });
    let res = await get();
    if (res.status === 401 && (await refreshSession())) res = await get();
    if (!res.ok) return notify.error(t("common.errorGeneric"));
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement("a");
    a.href = url;
    a.download = `attendance-${range}.xlsx`;
    a.click();
    URL.revokeObjectURL(url);
  };
  return (
    <div className="space-y-5">
      <header>
        <h1 className="text-2xl font-extrabold">{t("rep.title")}</h1>
        <p className="mt-1 max-w-prose text-muted-foreground">{t("rep.subtitle")}</p>
      </header>
      <div className="flex flex-wrap items-end gap-4">
        <div className="w-48">
          <JalaliDateField label={t("rep.from")} help={t("rep.fromHelp")} value={from} onChange={setFrom} />
        </div>
        <div className="w-48">
          <JalaliDateField label={t("rep.to")} help={t("rep.toHelp")} value={to} onChange={setTo} />
        </div>
        <Button variant="outline" className="h-12 md:h-11" disabled={!range} onClick={download} data-testid="report-excel">
          <Download aria-hidden="true" />
          {t("rep.excel")}
        </Button>
      </div>
      <div className="overflow-x-auto rounded-xl border bg-card" data-testid="report">
        <table className="w-full min-w-[720px] text-sm">
          <thead className="border-b bg-muted/40">
            <tr>
              {["rep.name", "rep.worked", "rep.late", "rep.early", "rep.paid", "rep.unpaid", "rep.excused", "rep.unexcused"].map((k) => (
                <th key={k} scope="col" className="h-11 px-3 text-start font-semibold">
                  {t(k as Parameters<typeof t>[0])}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-y">
            {(rep.data ?? []).map((r) => (
              <tr key={r.userId}>
                <th scope="row" className="px-3 py-2 text-start font-semibold">
                  {r.name}
                </th>
                <td className="px-3 py-2">{hm(r.workedMinutes)}</td>
                <td className="px-3 py-2">{r.lateMinutes > 0 ? `${formatNumber(r.lateMinutes)}′ (${formatNumber(r.lateCount)})` : "—"}</td>
                <td className="px-3 py-2">{r.earlyLeaveMinutes > 0 ? `${formatNumber(r.earlyLeaveMinutes)}′` : "—"}</td>
                <td className="px-3 py-2">{r.paidLeaveMinutes > 0 ? hm(r.paidLeaveMinutes) : "—"}</td>
                <td className="px-3 py-2">{r.unpaidLeaveMinutes > 0 ? hm(r.unpaidLeaveMinutes) : "—"}</td>
                <td className="px-3 py-2">{formatNumber(r.excusedAbsences)}</td>
                <td className="px-3 py-2">{formatNumber(r.unexcusedAbsences)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
