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
import type { Absence, LeaveRequest } from "./types";

registerTexts(faM5);

const LeaveSheet = dynamic(() => import("./leave-sheet").then((m) => m.LeaveSheet), { ssr: false });

/** Managers: pending leave requests and absences to decide; register leave for someone who phoned in. */
export function LeaveApprovals({ myId, canApprove }: { myId?: string; canApprove: boolean }) {
  const { t, formatNumber, formatDate } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const leaves = useQuery({ queryKey: ["leave-pending"], queryFn: () => api<LeaveRequest[]>("/api/v1/leave/requests?pending=true") });
  const absences = useQuery({ queryKey: ["absences-open"], queryFn: () => api<Absence[]>("/api/v1/leave/absences?open=true") });
  useLive(["presence"], [["leave-pending"], ["absences-open"]]);
  const [registering, setRegistering] = useState(false);
  const [reassign, setReassign] = useState<{ title: string; stageName: string }[]>([]);
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: ["leave-pending"] });
    void qc.invalidateQueries({ queryKey: ["absences-open"] });
  };
  const decideLeave = async (id: string, approve: boolean) => {
    try {
      const r = await api<{ workToReassign: { title: string; stageName: string }[] }>(`/api/v1/leave/requests/${id}/decide`, { method: "POST", body: { approve } });
      setReassign(r.workToReassign);
      refresh();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  const decideAbsence = async (id: string, approve: boolean) => {
    try {
      await api(`/api/v1/leave/absences/${id}/decide`, { method: "POST", body: { approve } });
      refresh();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  const dur = (l: LeaveRequest) => (l.hourly ? t("leave.hours", { h: formatNumber(Math.round(l.minutes / 6) / 10) }) : t("leave.days", { d: formatNumber(Math.max(1, Math.round((new Date(l.to).getTime() - new Date(l.from).getTime()) / 864e5))) }));

  return (
    <div className="space-y-8">
      <section className="space-y-3" data-testid="leave-pending">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-lg font-bold">{t("leave.pending")}</h2>
          {canApprove && (
            <Button variant="outline" className="h-11" onClick={() => setRegistering(true)}>
              <CalendarPlus aria-hidden="true" />
              {t("leave.forUser")}
            </Button>
          )}
        </div>
        {reassign.length > 0 && (
          <div className="rounded-lg border border-amber-400 p-3 text-sm">
            <p className="font-medium">{t("leave.reassign")}</p>
            <ul className="mt-1 list-inside list-disc">
              {reassign.map((w, i) => (
                <li key={i}>
                  {w.title} — {w.stageName}
                </li>
              ))}
            </ul>
          </div>
        )}
        <ul className="divide-y rounded-xl border bg-card">
          {(leaves.data ?? []).map((l) => (
            <li key={l.id} className="space-y-2 px-4 py-3 text-sm">
              <p className="font-semibold">
                {l.userName} · <span style={{ color: l.color }}>{l.typeName}</span> · {formatDate(l.from, { weekday: "short", day: "numeric", month: "short" })} · {dur(l)}
              </p>
              <p className="text-muted-foreground">
                {l.reason}
                {l.missingDocument && <span className="ms-2 rounded bg-amber-200 px-1.5 text-xs text-black">{t("leave.missingDocument")}</span>}
              </p>
              {canApprove && l.userId !== myId && (
                <div className="flex gap-2">
                  <Button size="sm" className="h-10" onClick={() => decideLeave(l.id, true)} data-testid="leave-approve">
                    {t("leave.approve")}
                  </Button>
                  <Button size="sm" variant="outline" className="h-10" onClick={() => decideLeave(l.id, false)}>
                    {t("leave.reject")}
                  </Button>
                </div>
              )}
            </li>
          ))}
          {leaves.data?.length === 0 && <li className="px-4 py-3 text-sm text-muted-foreground">{t("live.reviewEmpty")}</li>}
        </ul>
      </section>

      <section className="space-y-3" data-testid="absences-open">
        <h2 className="text-lg font-bold">{t("abs.open")}</h2>
        <ul className="divide-y rounded-xl border bg-card">
          {(absences.data ?? []).map((a) => (
            <li key={a.id} className="space-y-2 px-4 py-3 text-sm">
              <p className="font-semibold">
                {a.userName} · {formatDate(a.day + "T12:00:00", { weekday: "short", day: "numeric", month: "short" })} · {t(`abs.status.${a.status}` as TextKey)}
              </p>
              {a.reason && <p className="text-muted-foreground">{a.reason}</p>}
              {canApprove && a.userId !== myId && (
                <div className="flex gap-2">
                  <Button size="sm" className="h-10" onClick={() => decideAbsence(a.id, true)}>
                    {t("abs.excuse")}
                  </Button>
                  <Button size="sm" variant="outline" className="h-10" onClick={() => decideAbsence(a.id, false)}>
                    {t("abs.unexcuse")}
                  </Button>
                </div>
              )}
            </li>
          ))}
          {absences.data?.length === 0 && <li className="px-4 py-3 text-sm text-muted-foreground">{t("live.reviewEmpty")}</li>}
        </ul>
      </section>
      {registering && <LeaveSheet canRegisterForOthers onClose={() => setRegistering(false)} onSaved={() => { setRegistering(false); notify.success(t("leave.registered")); refresh(); }} />}
    </div>
  );
}
