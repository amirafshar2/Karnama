"use client";

import { useQueryClient } from "@/lib/query";
import dynamic from "next/dynamic";
import { useState } from "react";
import { CrudList, type Column } from "@/components/app/crud-list";
import { useRequire } from "@/components/app/use-require";
import { useAuth } from "@/lib/auth";
import { faM2 } from "@/lib/i18n/fa-m2";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { hasPermission } from "@/lib/types";

registerTexts(faM2);

const ClientEditor = dynamic(() => import("./client-editor").then((m) => m.ClientEditor), { ssr: false });

type ClientItem = {
  id: string;
  businessName: string;
  industry: string | null;
  city: string | null;
  contactName: string | null;
  mobile: string | null;
  status: "active" | "paused" | "ended";
  accounts: { platformId: string; handle: string }[];
  activePackage: string | null;
  daysLeft: number | null;
  hasPortal: boolean;
  isDeleted: boolean;
  version: number;
};

export default function ClientsPage() {
  const allowed = useRequire([{ code: "clients.view", scope: "assigned" }]);
  const { me } = useAuth();
  const canManage = hasPermission(me, "clients.manage", "all");
  const { t, formatNumber } = useI18n();
  const qc = useQueryClient();
  const [editing, setEditing] = useState<string | null>(null);
  if (!allowed) return null;

  const pkg = (c: ClientItem) =>
    c.activePackage ? (
      <span>
        <bdi>{c.activePackage}</bdi>
        {c.daysLeft !== null && <span className={`block text-xs ${c.daysLeft <= 5 ? "font-bold text-destructive" : "text-muted-foreground"}`}>{t("clients.daysLeft", { days: formatNumber(c.daysLeft) })}</span>}
      </span>
    ) : (
      <span className="text-muted-foreground">{t("clients.noPackage")}</span>
    );
  const status = (c: ClientItem) => <span className={`inline-flex rounded-md px-2 py-0.5 text-xs font-medium ${c.status === "active" ? "bg-secondary text-secondary-foreground" : "border"}`}>{t(`clients.status.${c.status}` as TextKey)}</span>;
  const columns: Column<ClientItem>[] = [
    {
      header: t("clients.business"),
      cell: (c) => (
        <div>
          <div className="font-semibold">{c.businessName}</div>
          <div className="text-xs text-muted-foreground">{[c.industry, c.city].filter(Boolean).join("، ")}</div>
        </div>
      ),
    },
    { header: t("clients.contact"), cell: (c) => <div>{c.contactName}<div className="text-xs text-muted-foreground" dir="ltr">{c.mobile ?? t("clients.mobileHidden")}</div></div>, className: "hidden lg:table-cell" },
    { header: t("clients.package"), cell: pkg },
    { header: t("clients.status"), cell: status },
  ];

  return (
    <>
      <CrudList<ClientItem>
        testId="clients"
        title={t("clients.title")}
        subtitle={t("clients.subtitle")}
        path="/api/v1/clients"
        queryKey="clients"
        newLabel={canManage ? t("clients.new") : undefined}
        onNew={canManage ? () => setEditing("new") : undefined}
        onEdit={canManage ? (c) => setEditing(c.id) : undefined}
        canDelete={() => canManage}
        columns={columns}
        nameOf={(c) => c.businessName}
        emptyText={t("clients.empty")}
        exportPath={canManage ? "/api/v1/clients/export" : undefined}
        historyType={canManage ? "Client" : undefined}
        card={(c) => (
          <>
            <div className="flex items-start justify-between gap-2">
              <div className="font-semibold">{c.businessName}</div>
              {status(c)}
            </div>
            <p className="mt-1 text-sm text-muted-foreground">{[c.industry, c.city, c.contactName].filter(Boolean).join("، ")}</p>
            <p className="mt-1 text-sm">{pkg(c)}</p>
          </>
        )}
      />
      {editing && (
        <ClientEditor
          id={editing}
          onClose={() => setEditing(null)}
          onChanged={() => qc.invalidateQueries({ queryKey: ["clients"] })}
          onSaved={async (created) => {
            setEditing(null);
            await qc.invalidateQueries({ queryKey: ["clients"] });
            notify.success(created ? t("crud.created") : t("crud.saved"));
          }}
        />
      )}
    </>
  );
}
