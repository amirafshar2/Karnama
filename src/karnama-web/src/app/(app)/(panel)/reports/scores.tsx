"use client";

import { Award, ChevronDown } from "lucide-react";
import { useState } from "react";
import { ErrorText } from "@/components/app/error-text";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { useQuery } from "@/lib/query";
import { cn } from "@/lib/utils";
import type { Score, Scores, TrendPoint } from "./types";

/** Ranking (when visible) + each person's score with the "why" behind every indicator. */
export function ScoresView({ range }: { range: string }) {
  const { t, formatNumber } = useI18n();
  const { me } = useAuth();
  const q = useQuery({ queryKey: ["scores", range], queryFn: () => api<Scores>(`/api/v1/reports/scores?${range}`) });
  const [open, setOpen] = useState<string | null>(null);
  if (q.error) return <ErrorText error={q.error} />;
  if (!q.data) return <div className="h-64 animate-pulse rounded-xl bg-muted" />;
  const s = q.data;
  const mine = s.people.find((p) => p.userId === me?.id);
  const shown = open ?? (s.people.length === 1 ? s.people[0].userId : null);

  return (
    <div className="space-y-4" data-testid="scores">
      {!s.rankingVisible && s.people.length === 1 && <p className="text-sm text-muted-foreground">{t("score.onlyYou")}</p>}
      <ol className="space-y-2">
        {s.people.map((p, i) => {
          const isOpen = shown === p.userId && p.indicators.length > 0;
          return (
            <li key={p.userId} className={cn("rounded-xl border bg-card", p.userId === mine?.userId && s.people.length > 1 && "border-foreground")} data-testid={`score-${p.name}`}>
              <button type="button" className="flex min-h-16 w-full items-center gap-3 px-4 py-3 text-start disabled:cursor-default" disabled={p.indicators.length === 0}
                aria-expanded={isOpen} onClick={() => setOpen(isOpen ? "" : p.userId)}>
                {s.people.length > 1 && <span className="w-6 text-center text-sm font-bold text-muted-foreground">{p.enough ? formatNumber(i + 1) : "–"}</span>}
                <span className="min-w-0 flex-1">
                  <span className="block font-semibold">{p.name}</span>
                  {p.badges.length > 0 && (
                    <span className="mt-1 flex flex-wrap gap-1">
                      {p.badges.map((b) => (
                        <span key={b} className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-950 dark:bg-amber-950 dark:text-amber-100">
                          <Award aria-hidden="true" className="size-3" />
                          {b}
                        </span>
                      ))}
                    </span>
                  )}
                </span>
                <Total p={p} min={s.minSamples} />
                {p.indicators.length > 0 && <ChevronDown aria-hidden="true" className={cn("size-5 shrink-0 transition-transform", isOpen && "rotate-180")} />}
              </button>
              {isOpen && <Why p={p} min={s.minSamples} />}
            </li>
          );
        })}
      </ol>
    </div>
  );
}

function Total({ p, min }: { p: Score; min: number }) {
  const { t, formatNumber } = useI18n();
  if (!p.enough || p.total === null)
    return (
      <span className="text-end text-xs text-muted-foreground" title={t("score.notEnoughHelp", { n: formatNumber(min), samples: formatNumber(p.samples) })}>
        {t("score.notEnough")}
      </span>
    );
  return (
    <span className="text-end">
      <span className="block text-2xl font-extrabold tabular-nums" data-testid="score-total">{formatNumber(p.total)}</span>
      <span className="block text-xs text-muted-foreground">{t("score.total")}</span>
    </span>
  );
}

function Why({ p, min }: { p: Score; min: number }) {
  const { t, formatNumber } = useI18n();
  return (
    <div className="space-y-5 border-t px-4 py-4">
      {!p.enough && <p className="text-sm text-muted-foreground">{t("score.notEnoughHelp", { n: formatNumber(min), samples: formatNumber(p.samples) })}</p>}
      <section>
        <h3 className="mb-3 font-bold">{t("score.why")}</h3>
        <ul className="space-y-3" data-testid="score-why">
          {p.indicators.map((i) => (
            <li key={i.key} className="grid gap-1">
              <div className="flex items-baseline justify-between gap-2 text-sm">
                <span className="font-semibold">
                  {t(`score.ind.${i.key}` as TextKey)} <span className="text-xs font-normal text-muted-foreground">{t("score.weight", { n: formatNumber(i.weight) })}</span>
                </span>
                <span className="font-bold tabular-nums">{i.score === null ? "—" : formatNumber(i.score)}</span>
              </div>
              <div className="h-2 overflow-hidden rounded-full bg-muted" aria-hidden="true">
                <div className="h-full rounded-full bg-primary" style={{ width: `${i.score ?? 0}%` }} />
              </div>
              <p className="text-xs text-muted-foreground">
                {i.value} — {i.hint}
              </p>
            </li>
          ))}
        </ul>
      </section>
      <Trend userId={p.userId} />
    </div>
  );
}

function Trend({ userId }: { userId: string }) {
  const { t, formatNumber } = useI18n();
  const q = useQuery({ queryKey: ["score-trend", userId], queryFn: () => api<TrendPoint[]>(`/api/v1/reports/scores/${userId}/trend`) });
  if (!q.data) return null;
  return (
    <section>
      <h3 className="mb-3 font-bold">{t("score.trend")}</h3>
      <ol className="grid grid-cols-6 items-end gap-2" data-testid="score-trend">
        {q.data.map((x) => (
          <li key={x.from} className="flex flex-col items-center gap-1 text-xs">
            <span className="font-semibold tabular-nums">{x.total === null ? "—" : formatNumber(x.total)}</span>
            <span className="flex h-24 w-full items-end justify-center rounded bg-muted/50">
              <span className="w-3/5 rounded-t bg-primary" style={{ height: `${x.total ?? 0}%` }} />
            </span>
            <span className="text-muted-foreground">{x.label}</span>
          </li>
        ))}
      </ol>
    </section>
  );
}
