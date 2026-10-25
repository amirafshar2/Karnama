"use client";

import { useQueryClient } from "@/lib/query";
import dynamic from "next/dynamic";
import { useState } from "react";
import { CrudList, type Column } from "@/components/app/crud-list";
import { useRequire } from "@/components/app/use-require";
import { faM2 } from "@/lib/i18n/fa-m2";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";

registerTexts(faM2);

const ServiceEditor = dynamic(() => import("./service-editor").then((m) => m.ServiceEditor), { ssr: false });

type ServiceItem = { id: string; name: string; basePrice: number | null; consumesQuota: boolean; stageCount: number; fieldCount: number; isActive: boolean; isDeleted: boolean; version: number };

export default function ServicesPage() {
  const allowed = useRequire([{ code: "services.manage", scope: "all" }]);
  const { t, formatNumber } = useI18n();
  const qc = useQueryClient();
  const [editing, setEditing] = useState<string | null>(null);
  if (!allowed) return null;
  const price = (s: ServiceItem) => (s.basePrice ? t("crud.toman", { amount: formatNumber(s.basePrice / 10) }) : "—");
  const columns: Column<ServiceItem>[] = [
    { header: t("services.name"), cell: (s) => <span className="font-semibold">{s.name}</span> },
    { header: t("services.colStages"), cell: (s) => formatNumber(s.stageCount) },
    { header: t("services.colFields"), cell: (s) => formatNumber(s.fieldCount), className: "hidden lg:table-cell" },
    { header: t("packages.colPrice"), cell: price },
  ];
  return (
    <>
      <CrudList<ServiceItem>
        testId="services"
        title={t("services.title")}
        subtitle={t("services.subtitle")}
        path="/api/v1/services"
        queryKey="services"
        newLabel={t("services.new")}
        onNew={() => setEditing("new")}
        onEdit={(s) => setEditing(s.id)}
        columns={columns}
        nameOf={(s) => s.name}
        emptyText={t("services.empty")}
        exportPath="/api/v1/services/export"
        historyType="ServiceType"
        card={(s) => (
          <>
            <div className="font-semibold">{s.name}</div>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("services.colStages")}: {formatNumber(s.stageCount)} — {t("services.colFields")}: {formatNumber(s.fieldCount)} — {price(s)}
            </p>
          </>
        )}
      />
      {editing && (
        <ServiceEditor
          id={editing}
          onClose={() => setEditing(null)}
          onSaved={async (created) => {
            setEditing(null);
            await qc.invalidateQueries({ queryKey: ["services"] });
            notify.success(created ? t("crud.created") : t("crud.saved"));
          }}
        />
      )}
    </>
  );
}
