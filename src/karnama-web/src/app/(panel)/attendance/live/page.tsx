"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useRequire } from "@/components/app/use-require";
import { useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { hm, type Shift } from "@/lib/attendance";
import { useAuth } from "@/lib/auth";
import { faM4 } from "@/lib/i18n/fa-m4";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { hasPermission } from "@/lib/types";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";

registerTexts(faM4);

type Presence = { userId: string; name: string; state: "in" | "break" | "out" | "absent" | "expected" | "off"; since: string | null; workedMinutes: number; lateMinutes: number; trust: Shift["trust"] | null; needsReview: boolean; timerTitle: string | null; timerMinutes: number | null };
type Correction = { id: string; userId: string; userName: string; day: string; clockIn: string | null; clockOut: string | null; reason: string; status: string; createdAt: string };

const DOT: Record<Presence["state"], string> = { in: "bg-emerald-500", break: "bg-amber-400", out: "bg-muted-foreground", absent: "bg-destructive", expected: "bg-sky-400", off: "bg-muted" };

export default function LivePresencePage() {
  const allowed = useRequire([{ code: "attendance.view", scope: "all" }]);
  const { me } = useAuth();
  const canDecide = hasPermission(me, "attendance.correct", "all");
  const { t, formatNumber, formatDate } = useI18n();
  const errorMessage = useErrorMessage();
  const qc = useQueryClient();
  const today = new Date();
  const from = new Date(today.getTime() - 7 * 864e5).toISOString().slice(0, 10);
  const to = new Date(today.getTime() + 864e5).toISOString().slice(0, 10);
  const live = useQuery({ queryKey: ["presence"], enabled: allowed, queryFn: () => api<Presence[]>("/api/v1/attendance/live"), refetchInterval: 60_000 });
  const review = useQuery({ queryKey: ["presence-review"], enabled: allowed, queryFn: () => api<Shift[]>(`/api/v1/attendance/shifts?from=${from}&to=${to}&needsReview=true`) });
  const corrections = useQuery({ queryKey: ["presence-corrections"], enabled: allowed, queryFn: () => api<Correction[]>("/api/v1/attendance/corrections?pending=true") });
  useLive(["presence"], [["presence"], ["presence-review"], ["presence-corrections"]]);
  if (!allowed) return null;

  const decide = async (path: string, approve: boolean) => {
    try {
      await api(path, { method: "POST", body: { approve } });
      void qc.invalidateQueries({ queryKey: ["presence-review"] });
      void qc.invalidateQueries({ queryKey: ["presence-corrections"] });
      void qc.invalidateQueries({ queryKey: ["presence"] });
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  const time = (iso: string | null) => (iso ? formatDate(iso, { hour: "2-digit", minute: "2-digit" }) : "—");
  const dur = (min: number) => t("att.hours", { h: formatNumber(hm(min).h), m: formatNumber(hm(min).m) });

  return (
    <div className="space-y-8">
      <header>
        <h1 className="text-2xl font-extrabold">{t("live.title")}</h1>
        <p className="mt-1 max-w-prose text-muted-foreground">{t("live.subtitle")}</p>
      </header>

      <section>
        <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3" data-testid="presence">
          {(live.data ?? []).map((p) => (
            <li key={p.userId} className="rounded-xl border bg-card p-4" data-testid={`presence-${p.userId}`}>
              <div className="flex items-center justify-between gap-2">
                <span className="flex items-center gap-2 font-semibold">
                  <span className={cn("size-2.5 rounded-full", DOT[p.state])} aria-hidden="true" />
                  {p.name}
                </span>
                <span className="text-xs text-muted-foreground">{t(`live.state.${p.state}` as TextKey)}</span>
              </div>
              <p className="mt-2 text-sm text-muted-foreground">
                {p.since && <>{time(p.since)} · </>}
                {dur(p.workedMinutes)}
                {p.lateMinutes > 0 && <span className="ms-2 text-destructive">{t("att.late", { m: formatNumber(p.lateMinutes) })}</span>}
                {p.needsReview && <span className="ms-2 text-amber-600">{t("live.review")}</span>}
              </p>
              {p.timerTitle && <p className="mt-1 text-sm">⏱ {p.timerTitle} — {formatNumber(p.timerMinutes ?? 0)}′</p>}
            </li>
          ))}
        </ul>
      </section>

      <section className="space-y-3">
        <h2 className="text-lg font-bold">{t("live.review")}</h2>
        <ul className="divide-y rounded-xl border bg-card" data-testid="review">
          {(review.data ?? []).map((s) => (
            <li key={s.id} className="space-y-2 px-4 py-3 text-sm">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <span className="font-semibold">
                  {s.userName} · {formatDate(s.clockInAt, { weekday: "short", day: "numeric", month: "short" })} {time(s.clockInAt)} – {time(s.clockOutAt)}
                </span>
                <span className={cn("rounded-md px-2 py-0.5 text-xs font-bold", s.trust === "red" ? "bg-destructive text-white" : "bg-amber-400 text-black")}>{t(`att.trust.${s.trust}` as TextKey)}</span>
              </div>
              <p className="text-muted-foreground">{s.reviewReason}</p>
              <p className="text-xs text-muted-foreground">{s.methods.map((m) => `${m.passed ? "✓" : "✗"} ${m.evidence}`).join(" · ")}</p>
              <div className="flex gap-2">
                <Button size="sm" className="h-10" onClick={() => decide(`/api/v1/attendance/shifts/${s.id}/review`, true)}>
                  {t("live.accept")}
                </Button>
                <Button size="sm" variant="outline" className="h-10" onClick={() => decide(`/api/v1/attendance/shifts/${s.id}/review`, false)}>
                  {t("live.reject")}
                </Button>
              </div>
            </li>
          ))}
          {review.data?.length === 0 && <li className="px-4 py-3 text-sm text-muted-foreground">{t("live.reviewEmpty")}</li>}
        </ul>
      </section>

      <section className="space-y-3">
        <h2 className="text-lg font-bold">{t("live.corrections")}</h2>
        <ul className="divide-y rounded-xl border bg-card" data-testid="corrections">
          {(corrections.data ?? []).map((c) => (
            <li key={c.id} className="space-y-2 px-4 py-3 text-sm">
              <p className="font-semibold">
                {c.userName} · {formatDate(c.day + "T12:00:00", { weekday: "short", day: "numeric", month: "short" })} · {time(c.clockIn)} – {time(c.clockOut)}
              </p>
              <p className="text-muted-foreground">{c.reason}</p>
              {canDecide && c.userId !== me?.id && (
                <div className="flex gap-2">
                  <Button size="sm" className="h-10" onClick={() => decide(`/api/v1/attendance/corrections/${c.id}/decide`, true)}>
                    {t("live.approve")}
                  </Button>
                  <Button size="sm" variant="outline" className="h-10" onClick={() => decide(`/api/v1/attendance/corrections/${c.id}/decide`, false)}>
                    {t("live.reject")}
                  </Button>
                </div>
              )}
            </li>
          ))}
          {corrections.data?.length === 0 && <li className="px-4 py-3 text-sm text-muted-foreground">{t("live.reviewEmpty")}</li>}
        </ul>
      </section>
    </div>
  );
}
