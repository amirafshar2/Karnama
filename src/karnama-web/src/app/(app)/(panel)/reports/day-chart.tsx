"use client";

import { useState } from "react";
import { useI18n } from "@/lib/i18n/provider";
import type { DayPoint } from "./types";

/** Daily presence vs. useful (timer) hours. Same unit, one axis; the timer bar sits inside the presence bar. */
export function DayChart({ days }: { days: DayPoint[] }) {
  const { t, formatNumber, formatDate } = useI18n();
  const [hover, setHover] = useState<number | null>(null);
  const max = Math.max(60, ...days.map((d) => Math.max(d.presenceMinutes, d.timerMinutes)));
  const top = Math.ceil(max / 120) * 120;
  const H = 160, W = Math.max(days.length * 18, 300);
  const bw = W / days.length;
  const y = (m: number) => H - (m / top) * H;
  const h = (m: number) => `${formatNumber(Math.floor(m / 60))}:${formatNumber(m % 60).padStart(2, formatNumber(0))}`;
  const ticks = [0, top / 2, top];
  const cur = hover === null ? null : days[hover];
  return (
    <figure className="rounded-xl border bg-card p-4" data-testid="rpt-chart">
      <figcaption className="mb-1 flex flex-wrap items-baseline justify-between gap-2">
        <span className="font-bold">{t("rpt.chart")}</span>
        <span className="flex gap-4 text-xs text-muted-foreground">
          <span className="flex items-center gap-1.5"><i className="inline-block size-3 rounded-sm bg-foreground/15" />{t("rpt.presence")}</span>
          <span className="flex items-center gap-1.5"><i className="inline-block size-3 rounded-sm bg-primary" />{t("rpt.timer")}</span>
        </span>
      </figcaption>
      <p className="mb-3 text-xs text-muted-foreground">{t("rpt.chartHelp")}</p>
      <div className="relative" dir="ltr">
        <svg viewBox={`-34 -6 ${W + 40} ${H + 26}`} className="h-auto w-full" role="img" aria-label={t("rpt.chart")} onMouseLeave={() => setHover(null)}>
          {ticks.map((v) => (
            <g key={v}>
              <line x1={0} x2={W} y1={y(v)} y2={y(v)} className="stroke-border" strokeWidth={1} vectorEffect="non-scaling-stroke" />
              <text x={-6} y={y(v) + 4} textAnchor="end" className="fill-muted-foreground text-[10px]">{formatNumber(v / 60)}</text>
            </g>
          ))}
          {days.map((d, i) => (
            <g key={d.day} onMouseEnter={() => setHover(i)} onFocus={() => setHover(i)}>
              <rect x={i * bw} y={0} width={bw} height={H} fill="transparent" />
              {d.presenceMinutes > 0 && <rect x={i * bw + bw * 0.12} y={y(d.presenceMinutes)} width={bw * 0.76} height={H - y(d.presenceMinutes)} rx={2} className="fill-foreground/15" />}
              {d.timerMinutes > 0 && <rect x={i * bw + bw * 0.3} y={y(d.timerMinutes)} width={bw * 0.4} height={H - y(d.timerMinutes)} rx={2} className="fill-primary" />}
              {hover === i && <rect x={i * bw} y={0} width={bw} height={H} className="fill-foreground/5" />}
            </g>
          ))}
          {[0, Math.floor(days.length / 2), days.length - 1].map((i) => (
            <text key={i} x={i * bw + bw / 2} y={H + 16} textAnchor="middle" className="fill-muted-foreground text-[10px]">{formatDate(days[i].day, { day: "numeric", month: "short" })}</text>
          ))}
        </svg>
        {cur && (
          <div className="pointer-events-none absolute top-0 rounded-md border bg-popover px-3 py-2 text-xs shadow-sm" style={{ left: `${Math.min(80, (hover! / days.length) * 100)}%` }} dir="rtl">
            <div className="font-semibold">{formatDate(cur.day, { weekday: "long", day: "numeric", month: "long" })}</div>
            <div>{t("rpt.presence")}: {h(cur.presenceMinutes)}</div>
            <div>{t("rpt.timer")}: {h(cur.timerMinutes)}</div>
            <div>{t("rpt.done")}: {formatNumber(cur.done)}</div>
          </div>
        )}
      </div>
    </figure>
  );
}
