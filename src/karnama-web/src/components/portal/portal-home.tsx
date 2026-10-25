"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { Check, Download, Plus, RotateCcw } from "lucide-react";
import dynamic from "next/dynamic";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faPortal } from "@/lib/i18n/fa-portal";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { downloadFile } from "@/lib/upload";
import { useLive } from "@/lib/use-live";
import type { FileItem } from "@/lib/work-types";

registerTexts(faPortal);

const PortalOrder = dynamic(() => import("./portal-order").then((m) => m.PortalOrder), { ssr: false });
const PortalNewOrder = dynamic(() => import("./portal-new-order").then((m) => m.PortalNewOrder), { ssr: false });

type Home = {
  clientName: string;
  package: { name: string; daysLeft: number; totalDays: number; quotas: { serviceTypeId: string; serviceName: string; used: number; total: number }[] } | null;
  approvals: { workItemId: string; orderId: string; orderNumber: number; title: string; stageName: string; submittedAt: string | null; files: FileItem[] }[];
  orders: { id: string; number: number; title: string; serviceName: string; status: string; percent: number; stepLabel: string; createdAt: string }[];
};
const HOME = ["portal-home"];

/** The client's whole panel on one page: what needs them, what's in progress, what's left in the package. */
export function PortalHome() {
  const { t, formatNumber, formatDate } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const home = useQuery({ queryKey: HOME, queryFn: () => api<Home>("/api/v1/portal/home") });
  useLive(["work", "order"], [HOME]);
  const [note, setNote] = useState<Record<string, string | undefined>>({});
  const [open, setOpen] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const h = home.data;

  const decide = async (id: string, approve: boolean) => {
    try {
      await api(`/api/v1/portal/approvals/${id}`, { method: "POST", body: { approve, note: note[id] } });
      notify.success(approve ? t("portal.approved") : t("portal.changesSent"));
      setNote({ ...note, [id]: undefined });
      await qc.invalidateQueries({ queryKey: HOME });
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  if (!h) return <div className="h-64 animate-pulse rounded-xl bg-muted" />;
  return (
    <div className="space-y-8" data-testid="portal">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-extrabold sm:text-3xl" data-testid="dashboard-hello">{t("portal.hello", { name: h.clientName })}</h1>
          <p className="mt-1 text-muted-foreground">{t("portal.subtitle")}</p>
        </div>
        <Button className="h-12" onClick={() => setCreating(true)} data-testid="portal-new">
          <Plus aria-hidden="true" />
          {t("portal.new")}
        </Button>
      </header>

      <section aria-labelledby="appr" className="space-y-3">
        <h2 id="appr" className="text-lg font-bold">
          {t("portal.approvals")} {h.approvals.length > 0 && <span className="ms-1 rounded-full bg-accent px-2 text-sm text-accent-foreground">{formatNumber(h.approvals.length)}</span>}
        </h2>
        {h.approvals.length === 0 && <p className="rounded-xl border border-dashed p-6 text-center text-muted-foreground">{t("portal.noApprovals")}</p>}
        <ul className="space-y-3">
          {h.approvals.map((a) => (
            <li key={a.workItemId} className="space-y-3 rounded-xl border-2 border-accent bg-card p-4" data-testid={`approval-${a.workItemId}`}>
              <div>
                <p className="font-semibold">{a.title}</p>
                <p className="text-sm text-muted-foreground">
                  #{formatNumber(a.orderNumber)} · {a.stageName}
                </p>
              </div>
              {a.files.length > 0 && (
                <ul className="flex flex-wrap gap-2">
                  {a.files.map((f) => (
                    <li key={f.id}>
                      <Button variant="outline" size="sm" className="h-10" onClick={() => downloadFile(f.id, f.fileName)}>
                        <Download className="size-4" aria-hidden="true" />
                        <bdi>{f.fileName}</bdi>
                      </Button>
                    </li>
                  ))}
                </ul>
              )}
              {note[a.workItemId] !== undefined ? (
                <div className="space-y-2">
                  <label htmlFor={`n-${a.workItemId}`} className="text-sm font-medium">
                    {t("portal.changesNote")}
                  </label>
                  <textarea id={`n-${a.workItemId}`} rows={3} className="w-full rounded-md border bg-transparent p-2 text-base" value={note[a.workItemId]} onChange={(e) => setNote({ ...note, [a.workItemId]: e.target.value })} data-testid="portal-note" />
                  <div className="flex gap-2">
                    <Button className="h-12" disabled={!note[a.workItemId]?.trim()} onClick={() => decide(a.workItemId, false)} data-testid="portal-send-changes">
                      {t("portal.send")}
                    </Button>
                    <Button variant="ghost" className="h-12" onClick={() => setNote({ ...note, [a.workItemId]: undefined })}>
                      {t("common.cancel")}
                    </Button>
                  </div>
                </div>
              ) : (
                <div className="flex flex-wrap gap-2">
                  <Button className="h-12 flex-1 sm:flex-none" onClick={() => decide(a.workItemId, true)} data-testid="portal-approve">
                    <Check aria-hidden="true" />
                    {t("portal.approve")}
                  </Button>
                  <Button variant="outline" className="h-12 flex-1 sm:flex-none" onClick={() => setNote({ ...note, [a.workItemId]: "" })} data-testid="portal-changes">
                    <RotateCcw aria-hidden="true" />
                    {t("portal.changes")}
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="pkg" className="space-y-3 rounded-xl border bg-card p-5">
        <h2 id="pkg" className="text-lg font-bold">
          {t("portal.package")}
        </h2>
        {h.package ? (
          <>
            <p className="font-semibold">{h.package.name}</p>
            <div className="h-2 overflow-hidden rounded-full bg-muted" role="progressbar" aria-valuemin={0} aria-valuemax={h.package.totalDays} aria-valuenow={h.package.daysLeft}>
              <div className="h-full bg-primary" style={{ width: `${Math.max(0, Math.min(100, (h.package.daysLeft / Math.max(1, h.package.totalDays)) * 100))}%` }} />
            </div>
            <p className="text-sm text-muted-foreground">{t("portal.daysLeft", { days: formatNumber(h.package.daysLeft), total: formatNumber(h.package.totalDays) })}</p>
            <ul className="grid gap-2 sm:grid-cols-2">
              {h.package.quotas.map((q) => (
                <li key={q.serviceTypeId} className="flex items-center justify-between rounded-lg border px-3 py-2 text-sm">
                  <span>{q.serviceName}</span>
                  <span className={q.used >= q.total ? "font-bold text-destructive" : "text-muted-foreground"}>{t("portal.used", { used: formatNumber(q.used), total: formatNumber(q.total) })}</span>
                </li>
              ))}
            </ul>
          </>
        ) : (
          <p className="text-muted-foreground">{t("portal.noPackage")}</p>
        )}
      </section>

      <section aria-labelledby="ord" className="space-y-3">
        <h2 id="ord" className="text-lg font-bold">
          {t("portal.orders")}
        </h2>
        {h.orders.length === 0 && <p className="text-muted-foreground">{t("portal.noOrders")}</p>}
        <ul className="space-y-2">
          {h.orders.map((o) => (
            <li key={o.id}>
              <button type="button" className="w-full rounded-xl border bg-card p-4 text-start hover:border-foreground/40" onClick={() => setOpen(o.id)} data-testid={`portal-o-${o.id}`}>
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-semibold">
                    #{formatNumber(o.number)} — {o.title}
                  </span>
                  <span className={o.stepLabel === "yourApproval" ? "rounded-md bg-accent px-2 text-sm font-bold text-accent-foreground" : "text-sm text-muted-foreground"}>
                    {t(`portal.step.${o.stepLabel}` as TextKey)}
                  </span>
                </div>
                <p className="mt-1 text-sm text-muted-foreground">
                  {o.serviceName} · {formatDate(o.createdAt, { day: "numeric", month: "long" })}
                </p>
                <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-muted" role="progressbar" aria-valuenow={o.percent} aria-valuemin={0} aria-valuemax={100}>
                  <div className="h-full bg-primary" style={{ width: `${o.percent}%` }} />
                </div>
              </button>
            </li>
          ))}
        </ul>
      </section>

      {open && <PortalOrder id={open} onClose={() => setOpen(null)} />}
      {creating && (
        <PortalNewOrder
          onClose={() => setCreating(false)}
          onCreated={async () => {
            setCreating(false);
            notify.success(t("portal.created"));
            await qc.invalidateQueries({ queryKey: HOME });
          }}
        />
      )}
    </div>
  );
}
