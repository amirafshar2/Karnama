"use client";

import { useQueryClient } from "@tanstack/react-query";
import dynamic from "next/dynamic";
import { useState } from "react";
import { CrudList, type Column } from "@/components/app/crud-list";
import { useRequire } from "@/components/app/use-require";
import { useAuth } from "@/lib/auth";
import { faM3 } from "@/lib/i18n/fa-m3";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { hasPermission } from "@/lib/types";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";

registerTexts(faM3);

const OrderCreate = dynamic(() => import("./order-create").then((m) => m.OrderCreate), { ssr: false });
const OrderSheet = dynamic(() => import("./order-sheet").then((m) => m.OrderSheet), { ssr: false });

type OrderItem = {
  id: string;
  number: number;
  title: string;
  clientName: string;
  serviceName: string;
  status: "submitted" | "inProgress" | "completed" | "rejected" | "cancelled";
  priority: "normal" | "high" | "urgent";
  source: "admin" | "client";
  deadline: string | null;
  outsidePackage: boolean;
  stagesDone: number;
  stagesTotal: number;
  currentStage: string | null;
  isDeleted?: boolean;
  version: number;
};

const STATUSES = ["submitted", "inProgress", "completed", "rejected", "cancelled"] as const;

export default function OrdersPage() {
  const allowed = useRequire([{ code: "orders.view", scope: "assigned" }]);
  const { me } = useAuth();
  const canCreate = hasPermission(me, "orders.create", "all");
  const canManage = hasPermission(me, "orders.manage", "all");
  const { t, formatNumber } = useI18n();
  const qc = useQueryClient();
  const [creating, setCreating] = useState(false);
  const [open, setOpen] = useState<string | null>(null);
  const [status, setStatus] = useState<string>("");
  useLive(["order"], [["orders"]]);
  if (!allowed) return null;

  const badge = (o: OrderItem) => (
    <span className={cn("inline-flex rounded-md px-2 py-0.5 text-xs font-medium", o.status === "submitted" ? "bg-accent text-accent-foreground" : o.status === "inProgress" ? "bg-secondary text-secondary-foreground" : "border")}>
      {t(`orders.status.${o.status}` as TextKey)}
    </span>
  );
  const progress = (o: OrderItem) =>
    o.stagesTotal > 0 ? (
      <div className="min-w-28">
        <div className="text-xs text-muted-foreground">{t("orders.progress", { done: formatNumber(o.stagesDone), total: formatNumber(o.stagesTotal) })}</div>
        <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-muted">
          <div className="h-full bg-primary" style={{ width: `${(o.stagesDone / o.stagesTotal) * 100}%` }} />
        </div>
        {o.currentStage && o.status === "inProgress" && <div className="mt-0.5 text-xs">{o.currentStage}</div>}
      </div>
    ) : (
      "—"
    );
  const columns: Column<OrderItem>[] = [
    {
      header: t("orders.number"),
      cell: (o) => (
        <div>
          <div className="font-semibold">
            #{formatNumber(o.number)} — {o.title}
          </div>
          <div className="text-xs text-muted-foreground">
            <bdi>{o.clientName}</bdi> · {o.serviceName}
            {o.outsidePackage && <span className="ms-2 text-destructive">{t("orders.outside")}</span>}
            {o.source === "client" && <span className="ms-2">{t("orders.fromClient")}</span>}
          </div>
        </div>
      ),
    },
    { header: t("orders.stages"), cell: progress, className: "hidden lg:table-cell" },
    { header: t("orders.status"), cell: badge },
  ];

  return (
    <>
      <CrudList<OrderItem>
        testId="orders"
        title={t("orders.title")}
        subtitle={t("orders.subtitle")}
        path="/api/v1/orders"
        queryKey="orders"
        params={{ status: status || undefined }}
        newLabel={canCreate ? t("orders.new") : undefined}
        onNew={canCreate ? () => setCreating(true) : undefined}
        onEdit={(o) => setOpen(o.id)}
        canDelete={() => false}
        deletable={false}
        editLabel={t("orders.open")}
        columns={columns}
        nameOf={(o) => o.title}
        emptyText={t("orders.empty")}
        historyType={canManage ? "Order" : undefined}
        filters={
          <select value={status} onChange={(e) => setStatus(e.target.value)} className="h-12 rounded-md border bg-card px-3 text-sm md:h-11" aria-label={t("orders.status")} data-testid="orders-status">
            <option value="">{t("orders.all")}</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {t(`orders.status.${s}` as TextKey)}
              </option>
            ))}
          </select>
        }
        card={(o) => (
          <>
            <div className="flex items-start justify-between gap-2">
              <div className="font-semibold">
                #{formatNumber(o.number)} — {o.title}
              </div>
              {badge(o)}
            </div>
            <p className="mt-1 text-sm text-muted-foreground">
              <bdi>{o.clientName}</bdi> · {o.serviceName}
            </p>
            <div className="mt-2">{progress(o)}</div>
          </>
        )}
      />
      {creating && (
        <OrderCreate
          onClose={() => setCreating(false)}
          onCreated={async (id) => {
            setCreating(false);
            await qc.invalidateQueries({ queryKey: ["orders"] });
            notify.success(t("orders.created"));
            setOpen(id);
          }}
        />
      )}
      {open && <OrderSheet id={open} onClose={() => setOpen(null)} />}
    </>
  );
}
