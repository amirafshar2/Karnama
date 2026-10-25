"use client";

import { useQuery } from "@/lib/query";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { useState } from "react";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faM5 } from "@/lib/i18n/fa-m5";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { toGregorian, toJalali } from "@/lib/jalali";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";

registerTexts(faM5);

type Row = { userId: string; name: string; days: { day: string; state: string; color: string | null; label: string | null }[] };
const pad = (n: number) => String(n).padStart(2, "0");
const iso = (g: { gy: number; gm: number; gd: number }) => `${g.gy}-${pad(g.gm)}-${pad(g.gd)}`;
const STYLE: Record<string, string> = {
  present: "bg-emerald-500",
  late: "bg-amber-400",
  absent: "bg-destructive",
  excused: "bg-amber-200",
  holiday: "bg-muted-foreground/40",
  none: "bg-muted",
};

/** Persian-month grid: one row per person, one cell per day (docs/PROMPT.md §6.7 team calendar). */
export default function AttendanceCalendarPage() {
  const allowed = useRequire([{ code: "attendance.self", scope: "own" }]);
  const { t, formatNumber, formatDate } = useI18n();
  const now = new Date();
  const j = toJalali(now.getFullYear(), now.getMonth() + 1, now.getDate());
  const [ym, setYm] = useState({ y: j.jy, m: j.jm });
  const days = ym.m <= 6 ? 31 : ym.m < 12 ? 30 : iso(toGregorian(ym.y, 12, 30)) === iso(toGregorian(ym.y + 1, 1, 1)) ? 29 : 30;
  const from = iso(toGregorian(ym.y, ym.m, 1));
  const to = iso(toGregorian(ym.y, ym.m, days));
  const cal = useQuery({ queryKey: ["att-calendar", from], enabled: allowed, queryFn: () => api<Row[]>(`/api/v1/leave/calendar?from=${from}&to=${to}`) });
  useLive(["presence"], [["att-calendar"]]);
  if (!allowed) return null;
  const move = (d: number) => setYm(({ y, m }) => (m + d > 12 ? { y: y + 1, m: 1 } : m + d < 1 ? { y: y - 1, m: 12 } : { y, m: m + d }));
  const title = formatDate(`${from}T12:00:00`, { month: "long", year: "numeric" });

  return (
    <div className="space-y-5">
      <header>
        <h1 className="text-2xl font-extrabold">{t("cal.title")}</h1>
        <p className="mt-1 max-w-prose text-muted-foreground">{t("cal.subtitle")}</p>
      </header>
      <div className="flex items-center gap-2">
        <Button variant="outline" size="icon" className="size-11" onClick={() => move(-1)} aria-label={t("cal.prev")}>
          <ChevronRight aria-hidden="true" className="rtl:rotate-0 ltr:rotate-180" />
        </Button>
        <p className="min-w-40 text-center font-bold" aria-live="polite">
          {title}
        </p>
        <Button variant="outline" size="icon" className="size-11" onClick={() => move(1)} aria-label={t("cal.next")}>
          <ChevronLeft aria-hidden="true" className="rtl:rotate-0 ltr:rotate-180" />
        </Button>
      </div>
      <div className="overflow-x-auto rounded-xl border bg-card" data-testid="calendar">
        <table className="min-w-max border-collapse text-xs">
          <thead>
            <tr>
              <th scope="col" className="sticky start-0 z-10 bg-card px-3 py-2 text-start font-semibold">
                <span className="sr-only">{t("rep.name")}</span>
              </th>
              {Array.from({ length: days }, (_, i) => (
                <th key={i} scope="col" className="w-8 px-0.5 py-2 text-center font-medium text-muted-foreground">
                  {formatNumber(i + 1)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {(cal.data ?? []).map((r) => (
              <tr key={r.userId} className="border-t">
                <th scope="row" className="sticky start-0 z-10 whitespace-nowrap bg-card px-3 py-2 text-start text-sm font-semibold">
                  {r.name}
                </th>
                {r.days.map((d) => {
                  const label = d.label ?? t(`cal.state.${d.state}` as TextKey);
                  return (
                    <td key={d.day} className="px-0.5 py-1 text-center">
                      <span
                        title={`${formatDate(d.day + "T12:00:00", { weekday: "long", day: "numeric", month: "long" })}: ${label}`}
                        aria-label={label}
                        className={cn("mx-auto block size-6 rounded", STYLE[d.state] ?? "", d.state === "hourlyLeave" && "ring-2 ring-inset")}
                        style={d.color ? { background: d.state === "hourlyLeave" ? undefined : d.color, ["--tw-ring-color" as string]: d.color } : undefined}
                      />
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <ul className="flex flex-wrap gap-4 text-xs text-muted-foreground" aria-label="legend">
        {["present", "late", "leave", "hourlyLeave", "absent", "excused", "holiday"].map((s) => (
          <li key={s} className="flex items-center gap-1.5">
            <span className={cn("size-3 rounded", STYLE[s] ?? "bg-sky-500", s === "hourlyLeave" && "bg-transparent ring-2 ring-sky-500 ring-inset")} aria-hidden="true" />
            {t(`cal.state.${s}` as TextKey)}
          </li>
        ))}
      </ul>
    </div>
  );
}
