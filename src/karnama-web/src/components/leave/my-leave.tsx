"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { CalendarPlus } from "lucide-react";
import dynamic from "next/dynamic";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faM5 } from "@/lib/i18n/fa-m5";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";
import type { Absence, Balance, LeaveRequest } from "./types";

registerTexts(faM5);

const LeaveSheet = dynamic(() => import("./leave-sheet").then((m) => m.LeaveSheet), { ssr: false });

/** On the worker's attendance page: balance, ask for leave, my requests, and absences that still need a reason. */
export function MyLeave() {
  const { t, formatNumber, formatDate } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const balance = useQuery({ queryKey: ["leave-balance"], queryFn: () => api<Balance[]>("/api/v1/leave/balance") });
  const mine = useQuery({ queryKey: ["leave-mine"], queryFn: () => api<LeaveRequest[]>("/api/v1/leave/requests") });
  const absences = useQuery({ queryKey: ["absences-mine"], queryFn: () => api<Absence[]>("/api/v1/leave/absences?open=true") });
  useLive(["presence"], [["leave-mine"], ["absences-mine"], ["leave-balance"]]);
  const [asking, setAsking] = useState(false);
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: ["leave-mine"] });
    void qc.invalidateQueries({ queryKey: ["leave-balance"] });
    void qc.invalidateQueries({ queryKey: ["absences-mine"] });
  };
  const span = (l: LeaveRequest) =>
    l.hourly
      ? `${formatDate(l.from, { weekday: "short", day: "numeric", month: "short" })} ${formatDate(l.from, { hour: "2-digit", minute: "2-digit" })}–${formatDate(l.to, { hour: "2-digit", minute: "2-digit" })}`
      : `${formatDate(l.from, { day: "numeric", month: "short" })} – ${formatDate(new Date(new Date(l.to).getTime() - 1), { day: "numeric", month: "short" })}`;

  return (
    <section className="space-y-4" aria-labelledby="leave-h" data-testid="my-leave">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="leave-h" className="font-bold">
          {t("leave.title")}
        </h2>
        <Button className="h-11" onClick={() => setAsking(true)} data-testid="leave-open">
          <CalendarPlus aria-hidden="true" />
          {t("leave.request")}
        </Button>
      </div>

      {(absences.data ?? []).filter((a) => a.status === "detected").map((a) => <AbsenceReason key={a.id} a={a} onSent={refresh} />)}

      {balance.data && balance.data.length > 0 && (
        <ul className="grid gap-2 sm:grid-cols-2">
          {balance.data.map((b) => (
            <li key={b.leaveTypeId} className="flex items-center justify-between rounded-lg border bg-card px-3 py-2 text-sm">
              <span className="flex items-center gap-2">
                <span className="size-2.5 rounded-full" style={{ background: b.color }} aria-hidden="true" />
                {b.typeName}
              </span>
              <span className="text-muted-foreground">
                {b.allowanceDays !== null ? t("leave.daysLeft", { left: formatNumber(b.leftDays ?? 0), total: formatNumber(b.allowanceDays) }) : t("leave.daysUsed", { used: formatNumber(b.usedDays) })}
              </span>
            </li>
          ))}
        </ul>
      )}

      <ul className="divide-y rounded-xl border bg-card">
        {(mine.data ?? []).slice(0, 10).map((l) => (
          <li key={l.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-3 text-sm">
            <span className="flex items-center gap-2">
              <span className="size-2.5 rounded-full" style={{ background: l.color }} aria-hidden="true" />
              {l.typeName} · {span(l)}
            </span>
            <span className="flex items-center gap-2">
              <span className={cn("rounded-md px-2 py-0.5 text-xs font-medium", l.status === "approved" ? "bg-emerald-600 text-white" : l.status === "pending" ? "bg-accent text-accent-foreground" : "border")}>
                {t(`leave.status.${l.status}` as TextKey)}
              </span>
              {(l.status === "pending" || (l.status === "approved" && new Date(l.from) > new Date())) && !l.byManager && (
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-9 text-destructive"
                  onClick={async () => {
                    try {
                      await api(`/api/v1/leave/requests/${l.id}/cancel`, { method: "POST" });
                      refresh();
                    } catch (e) {
                      notify.error(errorMessage(e));
                    }
                  }}
                >
                  {t("leave.cancel")}
                </Button>
              )}
            </span>
          </li>
        ))}
        {mine.data?.length === 0 && <li className="px-4 py-3 text-sm text-muted-foreground">{t("leave.none")}</li>}
      </ul>
      {asking && (
        <LeaveSheet
          canRegisterForOthers={false}
          onClose={() => setAsking(false)}
          onSaved={() => {
            setAsking(false);
            notify.success(t("leave.sent"));
            refresh();
          }}
        />
      )}
    </section>
  );
}

function AbsenceReason({ a, onSent }: { a: Absence; onSent: () => void }) {
  const { t, formatDate } = useI18n();
  const errorMessage = useErrorMessage();
  const [reason, setReason] = useState("");
  const send = async () => {
    try {
      await api(`/api/v1/leave/absences/${a.id}/reason`, { method: "POST", body: { reason } });
      notify.success(t("abs.sent"));
      onSent();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  return (
    <div className="space-y-2 rounded-xl border-2 border-amber-400 bg-amber-50 p-4 text-sm dark:bg-amber-950" data-testid="absence-reason">
      <p className="font-semibold">{t("abs.detected", { day: formatDate(a.day + "T12:00:00", { weekday: "long", day: "numeric", month: "long" }), time: formatDate(a.expectedAt, { hour: "2-digit", minute: "2-digit" }) })}</p>
      <p>{t("abs.deadline", { when: formatDate(a.reasonDeadline, { weekday: "long", hour: "2-digit", minute: "2-digit" }) })}</p>
      <label htmlFor={`abs-${a.id}`} className="block font-medium">
        {t("abs.reason")}
      </label>
      <textarea id={`abs-${a.id}`} rows={2} className="w-full rounded-md border bg-background p-2 text-base" value={reason} onChange={(e) => setReason(e.target.value)} aria-describedby={`abs-h-${a.id}`} />
      <p id={`abs-h-${a.id}`} className="text-xs text-muted-foreground">
        {t("abs.reasonHelp")}
      </p>
      <Button className="h-11" disabled={reason.trim().length < 3} onClick={send}>
        {t("abs.send")}
      </Button>
    </div>
  );
}
