"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import { useState } from "react";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { FilesPanel } from "@/components/app/files-panel";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { useIsMobile } from "@/hooks/use-mobile";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { hasPermission } from "@/lib/types";
import { cn } from "@/lib/utils";
import type { OrderDetail } from "@/lib/work-types";

export function OrderSheet({ id, onClose }: { id: string; onClose: () => void }) {
  const { t, dir, formatNumber, formatDate } = useI18n();
  const { me } = useAuth();
  const canManage = hasPermission(me, "orders.manage", "all");
  const isMobile = useIsMobile();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const q = useQuery({ queryKey: ["order", id], queryFn: () => api<OrderDetail>(`/api/v1/orders/${id}`) });
  const [busy, setBusy] = useState(false);
  const [reject, setReject] = useState<string | null>(null);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const o = q.data;

  const run = async (path: string, body?: unknown, ok?: string) => {
    setBusy(true);
    try {
      qc.setQueryData(["order", id], await api<OrderDetail>(path, { method: "POST", body }));
      void qc.invalidateQueries({ queryKey: ["orders"] });
      void qc.invalidateQueries({ queryKey: ["work-board"] });
      if (ok) notify.success(ok);
      setReject(null);
      setConfirmCancel(false);
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Sheet open onOpenChange={(v) => !v && onClose()}>
      <SheetContent side={isMobile ? "bottom" : dir === "rtl" ? "left" : "right"} className={cn("flex flex-col gap-0 p-0", isMobile ? "h-[94dvh] rounded-t-2xl" : "w-full sm:max-w-2xl")}>
        <SheetHeader className="border-b p-5">
          <SheetTitle className="text-xl">{o ? `${t("orders.detail", { number: formatNumber(o.number) })} — ${o.title}` : t("common.loading")}</SheetTitle>
          {o && (
            <SheetDescription>
              <bdi>{o.clientName}</bdi> · {o.serviceName} · {t(`orders.status.${o.status}` as TextKey)}
              {o.outsidePackage && <span className="ms-2 text-destructive">{t("orders.outside")}</span>}
            </SheetDescription>
          )}
        </SheetHeader>
        {!o ? (
          <div className="grid flex-1 place-items-center">{q.error ? <ErrorText error={q.error} /> : <Loader2 className="animate-spin" aria-label={t("common.loading")} />}</div>
        ) : (
          <div className="flex-1 space-y-6 overflow-y-auto p-5" data-testid="order-sheet">
            {canManage && o.status === "submitted" && (
              <section className="space-y-2 rounded-xl border bg-accent/10 p-4">
                {reject !== null ? (
                  <>
                    <label htmlFor="reject-reason" className="text-sm font-medium">
                      {t("orders.rejectReason")}
                    </label>
                    <textarea id="reject-reason" rows={3} className="w-full rounded-md border bg-background p-2 text-base md:text-sm" value={reject} onChange={(e) => setReject(e.target.value)} />
                    <div className="flex gap-2">
                      <Button type="button" variant="destructive" className="h-11" disabled={busy || !reject.trim()} onClick={() => run(`/api/v1/orders/${id}/reject`, { reason: reject })}>
                        {t("orders.reject")}
                      </Button>
                      <Button type="button" variant="ghost" className="h-11" onClick={() => setReject(null)}>
                        {t("common.cancel")}
                      </Button>
                    </div>
                  </>
                ) : (
                  <div className="flex flex-wrap gap-2">
                    <Button type="button" className="h-12 md:h-11" disabled={busy} onClick={() => run(`/api/v1/orders/${id}/accept?version=${o.version}`, undefined, t("orders.accepted"))} data-testid="order-accept">
                      {t("orders.accept")}
                    </Button>
                    <Button type="button" variant="outline" className="h-12 md:h-11" onClick={() => setReject("")}>
                      {t("orders.reject")}
                    </Button>
                  </div>
                )}
              </section>
            )}
            {o.rejectReason && <p className="rounded-lg border border-destructive/40 p-3 text-sm">{o.rejectReason}</p>}

            {o.stages.length > 0 && (
              <section className="space-y-2">
                <h3 className="font-bold">{t("orders.stages")}</h3>
                <ol className="space-y-1">
                  {o.stages.map((s) => (
                    <li key={s.id} className="flex flex-wrap items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm">
                      <span>
                        {formatNumber(s.order)}. {s.name}
                      </span>
                      <span className="text-muted-foreground">
                        {t(`work.col.${s.status}` as TextKey)} · {s.assigneeName ?? t("work.unassigned")}
                        {s.deadline && ` · ${formatDate(s.deadline, { day: "numeric", month: "short" })}`}
                      </span>
                    </li>
                  ))}
                </ol>
              </section>
            )}

            <section className="space-y-2">
              <h3 className="font-bold">{t("orders.answers")}</h3>
              <dl className="grid gap-x-4 gap-y-2 rounded-lg border p-3 text-sm sm:grid-cols-[auto_1fr]">
                {o.answers.map((a) => (
                  <div key={a.key} className="contents">
                    <dt className="text-muted-foreground">{a.label}</dt>
                    <dd className="whitespace-pre-wrap">{Array.isArray(a.value) ? a.value.join("، ") : a.value === null || a.value === undefined ? "—" : String(a.value)}</dd>
                  </div>
                ))}
                {o.note && (
                  <div className="contents">
                    <dt className="text-muted-foreground">{t("orders.note")}</dt>
                    <dd className="whitespace-pre-wrap">{o.note}</dd>
                  </div>
                )}
              </dl>
            </section>

            <section className="space-y-2">
              <h3 className="font-bold">{t("work.files")}</h3>
              <FilesPanel files={o.files} target={{ orderId: o.id, purpose: "brief" }} canUpload={canManage && o.status !== "cancelled" && o.status !== "rejected"} canShare={canManage} onChanged={() => qc.invalidateQueries({ queryKey: ["order", id] })} />
            </section>

            {canManage && (o.status === "submitted" || o.status === "inProgress") && (
              <section className="border-t pt-4">
                {confirmCancel ? (
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-sm">{t("orders.cancelConfirm")}</span>
                    <Button type="button" variant="destructive" className="h-11" disabled={busy} onClick={() => run(`/api/v1/orders/${id}/cancel`)}>
                      {t("crud.confirmYes")}
                    </Button>
                    <Button type="button" variant="ghost" className="h-11" onClick={() => setConfirmCancel(false)}>
                      {t("common.cancel")}
                    </Button>
                  </div>
                ) : (
                  <Button type="button" variant="ghost" className="h-11 text-destructive" onClick={() => setConfirmCancel(true)}>
                    {t("orders.cancel")}
                  </Button>
                )}
              </section>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}
