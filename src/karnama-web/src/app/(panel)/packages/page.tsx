"use client";

import { useQueryClient } from "@tanstack/react-query";
import dynamic from "next/dynamic";
import { useState } from "react";
import { CrudList, type Column } from "@/components/app/crud-list";
import { useRequire } from "@/components/app/use-require";
import { hasPermission } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { faM2 } from "@/lib/i18n/fa-m2";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { API_BASE } from "@/lib/config";

registerTexts(faM2);

const PackageEditor = dynamic(() => import("./package-editor").then((m) => m.PackageEditor), { ssr: false });

export type PackageItem = {
  id: string;
  name: string;
  sku: string;
  category: string | null;
  price: number;
  specialPrice: number | null;
  effectivePrice: number;
  period: string;
  validityDays: number;
  status: "draft" | "active" | "archived";
  badge: string;
  showOnSite: boolean;
  activeClients: number;
  capacity: number | null;
  quotas: { serviceTypeId: string; count: number; serviceName: string | null }[];
  coverUrl: string | null;
  isDeleted: boolean;
  version: number;
};

export default function PackagesPage() {
  const allowed = useRequire([{ code: "packages.view", scope: "all" }]);
  const { me } = useAuth();
  const canManage = hasPermission(me, "packages.manage", "all");
  const { t, formatNumber } = useI18n();
  const qc = useQueryClient();
  const [editing, setEditing] = useState<string | null>(null);
  if (!allowed) return null;

  const price = (p: PackageItem) => (
    <span className="inline-flex flex-wrap items-baseline gap-x-2 whitespace-nowrap">
      <span>{t("crud.toman", { amount: formatNumber(p.effectivePrice / 10) })}</span>
      {p.specialPrice !== null && p.effectivePrice < p.price && <s className="text-xs text-muted-foreground">{formatNumber(p.price / 10)}</s>}
    </span>
  );
  const quotas = (p: PackageItem) => p.quotas.map((q) => `${formatNumber(q.count)} ${q.serviceName ?? ""}`).join("، ");
  const status = (p: PackageItem) => (
    <span className={`inline-flex rounded-md px-2 py-0.5 text-xs font-medium ${p.status === "active" ? "bg-secondary text-secondary-foreground" : "border"}`}>{t(`packages.status.${p.status}` as TextKey)}</span>
  );
  const columns: Column<PackageItem>[] = [
    {
      header: t("packages.name"),
      cell: (p) => (
        <div className="flex items-center gap-3">
          {/* eslint-disable-next-line @next/next/no-img-element -- static export; media is served by the API */}
          {p.coverUrl && <img src={API_BASE + p.coverUrl} alt="" className="size-10 rounded-md object-cover" />}
          <div>
            <div className="font-semibold">{p.name}</div>
            <div className="text-xs text-muted-foreground">
              {t(`packages.period.${p.period}` as TextKey)} — {t("crud.days", { count: formatNumber(p.validityDays) })} — {t("packages.activeClients", { count: formatNumber(p.activeClients) })}
            </div>
          </div>
        </div>
      ),
    },
    { header: t("packages.colPrice"), cell: price },
    { header: t("packages.colQuotas"), cell: quotas, className: "hidden lg:table-cell" },
    { header: t("packages.colStatus"), cell: status },
  ];

  return (
    <>
      <CrudList<PackageItem>
        testId="packages"
        title={t("packages.title")}
        subtitle={t("packages.subtitle")}
        path="/api/v1/packages"
        queryKey="packages"
        newLabel={canManage ? t("packages.new") : undefined}
        onNew={canManage ? () => setEditing("new") : undefined}
        onEdit={canManage ? (p) => setEditing(p.id) : undefined}
        canDelete={() => canManage}
        columns={columns}
        nameOf={(p) => p.name}
        emptyText={t("packages.empty")}
        exportPath={canManage ? "/api/v1/packages/export" : undefined}
        historyType={canManage ? "Package" : undefined}
        card={(p) => (
          <>
            <div className="flex items-start justify-between gap-2">
              <div className="font-semibold">{p.name}</div>
              {status(p)}
            </div>
            <p className="mt-1 text-sm">{price(p)}</p>
            <p className="mt-1 text-sm text-muted-foreground">{quotas(p)}</p>
          </>
        )}
      />
      {editing && (
        <PackageEditor
          id={editing}
          onClose={() => setEditing(null)}
          onSaved={async (created) => {
            setEditing(null);
            await qc.invalidateQueries({ queryKey: ["packages"] });
            notify.success(created ? t("crud.created") : t("crud.saved"));
          }}
        />
      )}
    </>
  );
}
