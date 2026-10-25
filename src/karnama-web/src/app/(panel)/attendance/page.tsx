"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Coffee, LogIn, LogOut, MapPin, QrCode, Square, WifiOff } from "lucide-react";
import dynamic from "next/dynamic";
import { useCallback, useEffect, useRef, useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { flushQueue, hm, locate, queuedCount, sendClock, type ClockAction, type Evidence, type Shift, type Today } from "@/lib/attendance";
import { faM4 } from "@/lib/i18n/fa-m4";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";

registerTexts(faM4);

const QrScanner = dynamic(() => import("./qr-scanner").then((m) => m.QrScanner), { ssr: false });
const CorrectionSheet = dynamic(() => import("./correction-sheet").then((m) => m.CorrectionSheet), { ssr: false });
const ME = ["attendance-me"];

export function TrustBadge({ trust }: { trust: Shift["trust"] }) {
  const { t } = useI18n();
  return (
    <span className={cn("inline-flex rounded-md px-2 py-0.5 text-xs font-bold", trust === "green" ? "bg-emerald-600 text-white" : trust === "yellow" ? "bg-amber-400 text-black" : "bg-destructive text-white")}>
      {t(`att.trust.${trust}` as TextKey)}
    </span>
  );
}

export default function AttendancePage() {
  const allowed = useRequire([{ code: "attendance.self", scope: "own" }]);
  const { t, formatNumber, formatDate } = useI18n();
  const errorMessage = useErrorMessage();
  const qc = useQueryClient();
  const me = useQuery({ queryKey: ME, enabled: allowed, queryFn: () => api<Today>("/api/v1/attendance/me"), refetchInterval: 60_000 });
  useLive(["presence"], [ME]);
  const [busy, setBusy] = useState(false);
  const [scanning, setScanning] = useState<ClockAction | null>(null);
  const [early, setEarly] = useState<string | null>(null);
  const [correcting, setCorrecting] = useState(false);
  const [pending, setPending] = useState(0);

  // Send anything queued while offline as soon as we're back.
  useEffect(() => {
    const flush = async () => {
      setPending(queuedCount());
      if (!queuedCount()) return;
      const n = await flushQueue();
      setPending(queuedCount());
      if (n > 0) {
        notify.success(t("att.sent"));
        void qc.invalidateQueries({ queryKey: ME });
      }
    };
    void flush();
    window.addEventListener("online", flush);
    return () => window.removeEventListener("online", flush);
  }, [qc, t]);

  const d = me.data;
  const needsQr = d?.methods.includes("RotatingQr");
  const needsLocation = d?.methods.includes("Geofence");

  const act = useCallback(
    async (action: ClockAction, code?: string | null, note?: string) => {
      setBusy(true);
      try {
        const evidence: Evidence = {};
        if (code) evidence.qr = code;
        if ((action === "clock-in" || action === "clock-out") && needsLocation && !code) {
          const loc = await locate();
          if (loc) Object.assign(evidence, loc);
          else notify.message(t("att.locationFailed"));
        }
        const r = await sendClock(action, evidence, note);
        if (r.queued) {
          notify.message(t("att.queued"));
          setPending(queuedCount());
        } else {
          qc.setQueryData(ME, r.today);
          if (action === "clock-in") notify.success(t("att.clockedIn"));
          if (action === "clock-out") notify.success(t("att.clockedOut"));
        }
      } catch (e) {
        notify.error(errorMessage(e));
      } finally {
        setBusy(false);
        setScanning(null);
        setEarly(null);
      }
    },
    [errorMessage, needsLocation, qc, t],
  );

  // The scanner keeps one stable callback; the latest action and note are read from refs.
  const pendingAction = useRef<{ action: ClockAction | null; note?: string }>({ action: null });
  const actRef = useRef(act);
  useEffect(() => {
    pendingAction.current = { action: scanning, note: early ?? undefined };
    actRef.current = act;
  });
  const onCode = useCallback((code: string) => {
    const p = pendingAction.current;
    if (p.action) void actRef.current(p.action, code, p.note);
  }, []);

  if (!allowed) return null;
  const open = d?.shift;
  const time = (iso: string) => formatDate(iso, { hour: "2-digit", minute: "2-digit" });
  const dur = (min: number) => t("att.hours", { h: formatNumber(hm(min).h), m: formatNumber(hm(min).m) });

  // The big button never forces the camera; scanning the office QR is an extra button when the rule uses it.
  const start = (action: ClockAction) => act(action, null, early ?? undefined);

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <header>
        <h1 className="text-2xl font-extrabold">{t("att.title")}</h1>
        <p className="mt-1 text-muted-foreground">{t("att.subtitle")}</p>
      </header>

      {pending > 0 && (
        <p className="flex items-center gap-2 rounded-lg border border-amber-400 bg-amber-50 p-3 text-sm text-amber-900 dark:bg-amber-950 dark:text-amber-100">
          <WifiOff className="size-4" aria-hidden="true" />
          {t("att.queued")}
        </p>
      )}

      <section className="rounded-2xl border bg-card p-6 text-center" aria-live="polite" data-testid="clock-card">
        {!d ? (
          <div className="mx-auto h-24 w-48 animate-pulse rounded-xl bg-muted" />
        ) : (
          <>
            <p className="text-lg font-bold" data-testid="clock-state">
              {!open ? t("att.out") : open.onBreak ? t("att.onBreak", { time: time(open.clockInAt) }) : t("att.in", { time: time(open.clockInAt) })}
            </p>
            <p className="mt-1 text-sm text-muted-foreground">
              {d.isHoliday ? t("att.holiday") : d.schedule ? t("att.schedule", { start: d.schedule.start.slice(0, 5), end: d.schedule.end.slice(0, 5) }) : t("att.noSchedule")}
            </p>
            {open && open.trust !== "green" && (
              <p className="mt-2 text-sm">
                <TrustBadge trust={open.trust} /> {open.reviewReason}
              </p>
            )}

            {scanning ? (
              <div className="mx-auto mt-5 max-w-xs text-start">
                <QrScanner onCode={onCode} />
                <Button variant="ghost" className="mt-2 h-11 w-full" onClick={() => setScanning(null)}>
                  {t("common.cancel")}
                </Button>
              </div>
            ) : early !== null ? (
              <div className="mx-auto mt-5 max-w-sm space-y-2 text-start">
                <label htmlFor="early" className="text-sm font-medium">
                  {t("att.earlyReason")}
                </label>
                <textarea id="early" rows={2} className="w-full rounded-md border bg-transparent p-2 text-base" value={early} onChange={(e) => setEarly(e.target.value)} />
                <Button className="h-14 w-full text-lg" disabled={busy} onClick={() => start("clock-out")} data-testid="clock-out-confirm">
                  <LogOut aria-hidden="true" />
                  {t("att.clockOut")}
                </Button>
              </div>
            ) : (
              <div className="mt-5 flex flex-col items-center gap-3">
                {!open ? (
                  <>
                    <Button className="h-16 w-full max-w-xs rounded-2xl text-xl" disabled={busy} onClick={() => start("clock-in")} data-testid="clock-in">
                      <LogIn className="size-6" aria-hidden="true" />
                      {t("att.clockIn")}
                    </Button>
                    {needsQr && (
                      <Button variant="outline" className="h-12 w-full max-w-xs" disabled={busy} onClick={() => setScanning("clock-in")} data-testid="clock-in-qr">
                        <QrCode aria-hidden="true" />
                        {t("att.scanQr")}
                      </Button>
                    )}
                  </>
                ) : (
                  <>
                    <Button
                      className="h-16 w-full max-w-xs rounded-2xl text-xl"
                      variant="destructive"
                      disabled={busy}
                      onClick={() => {
                        const end = d.schedule && new Date(`${open.day}T${d.schedule.end}+03:30`);
                        if (end && !isNaN(end.getTime()) && Date.now() < end.getTime()) setEarly("");
                        else start("clock-out");
                      }}
                      data-testid="clock-out"
                    >
                      <LogOut className="size-6" aria-hidden="true" />
                      {t("att.clockOut")}
                    </Button>
                    <Button variant="outline" className="h-12 w-full max-w-xs" disabled={busy} onClick={() => act(open.onBreak ? "break/end" : "break/start")} data-testid="break">
                      <Coffee aria-hidden="true" />
                      {open.onBreak ? t("att.breakEnd") : t("att.breakStart")}
                    </Button>
                  </>
                )}
                {(needsQr || needsLocation) && (
                  <p className="flex flex-wrap justify-center gap-3 text-xs text-muted-foreground">
                    {needsQr && (
                      <span>
                        <QrCode className="me-1 inline size-3" aria-hidden="true" />
                        {t("att.how.RotatingQr")}
                      </span>
                    )}
                    {needsLocation && (
                      <span>
                        <MapPin className="me-1 inline size-3" aria-hidden="true" />
                        {t("att.locationAsk")}
                      </span>
                    )}
                  </p>
                )}
              </div>
            )}
          </>
        )}
      </section>

      {d && (
        <section className="grid grid-cols-2 gap-3">
          <div className="rounded-xl border bg-card p-4">
            <p className="text-sm text-muted-foreground">{t("att.today")}</p>
            <p className="mt-1 text-xl font-bold">{dur(d.workedMinutesToday)}</p>
          </div>
          <div className="rounded-xl border bg-card p-4">
            <p className="text-sm text-muted-foreground">{t("att.week")}</p>
            <p className="mt-1 text-xl font-bold">{dur(d.workedMinutesWeek)}</p>
          </div>
        </section>
      )}

      {d && (
        <section className="space-y-2 rounded-xl border bg-card p-4" data-testid="timer">
          <h2 className="font-bold">{t("att.timer")}</h2>
          {d.timer ? (
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-sm">{t("att.timerOn", { title: d.timer.title, minutes: formatNumber(d.timer.minutesToday) })}</p>
              <Button variant="outline" className="h-11" onClick={async () => qc.setQueryData(ME, await api<Today>("/api/v1/attendance/timer", { method: "DELETE" }))}>
                <Square className="size-4" aria-hidden="true" />
                {t("att.timerStop")}
              </Button>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">{t("att.timerNone")}</p>
          )}
        </section>
      )}

      {d && d.recentShifts.length > 0 && (
        <section className="space-y-2">
          <div className="flex items-center justify-between">
            <h2 className="font-bold">{t("att.recent")}</h2>
            <Button variant="ghost" className="h-11" onClick={() => setCorrecting(true)} data-testid="correction-open">
              {t("att.correction")}
            </Button>
          </div>
          <ul className="divide-y rounded-xl border bg-card">
            {d.recentShifts.map((s) => (
              <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-3 text-sm">
                <span>
                  {formatDate(s.clockInAt, { weekday: "short", day: "numeric", month: "short" })} · {time(s.clockInAt)} – {s.clockOutAt ? time(s.clockOutAt) : "…"}
                </span>
                <span className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                  {dur(s.workedMinutes)}
                  {s.lateMinutes > 0 && <span className="text-destructive">{t("att.late", { m: formatNumber(s.lateMinutes) })}</span>}
                  {s.offline && <span>{t("att.offlineTag")}</span>}
                  <TrustBadge trust={s.trust} />
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}
      <p className="text-xs text-muted-foreground">{t("att.privacy")}</p>
      {correcting && <CorrectionSheet onClose={() => setCorrecting(false)} />}
    </div>
  );
}
