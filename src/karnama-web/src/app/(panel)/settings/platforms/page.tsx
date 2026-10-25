"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faCrud } from "@/lib/i18n/fa-crud";
import { faM2 } from "@/lib/i18n/fa-m2";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import dynamic from "next/dynamic";

const PlatformEditor = dynamic(() => import("./platform-editor").then((m) => m.PlatformEditor), { ssr: false });

registerTexts(faM2);
registerTexts(faCrud);

export type Platform = { id: string; key: string; name: string; nameValues: Record<string, string>; urlTemplate: string | null; sortOrder: number; isActive: boolean; isDeleted: boolean; version: number };

export default function PlatformsPage() {
  const allowed = useRequire([{ code: "settings.manage", scope: "all" }]);
  const { t, lang } = useI18n();
  const errorMessage = useErrorMessage();
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ["platforms", "admin"], enabled: allowed, queryFn: () => api<Platform[]>("/api/v1/platforms?includeDeleted=true") });
  const [editing, setEditing] = useState<Platform | "new" | null>(null);
  if (!allowed) return null;
  const refresh = () => qc.invalidateQueries({ queryKey: ["platforms"] });

  const toggleDeleted = async (p: Platform) => {
    try {
      await api(`/api/v1/platforms/${p.id}${p.isDeleted ? "/restore" : ""}`, { method: p.isDeleted ? "POST" : "DELETE" });
      await refresh();
      notify.success(p.isDeleted ? t("crud.restored") : t("crud.deletedToast", { name: p.name }));
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-extrabold">{t("platforms.title")}</h1>
          <p className="mt-1 max-w-prose text-muted-foreground">{t("platforms.subtitle")}</p>
        </div>
        <Button className="h-12 md:h-11" onClick={() => setEditing("new")} data-testid="platform-new">
          <Plus aria-hidden="true" />
          {t("platforms.new")}
        </Button>
      </header>
      <ul className="divide-y rounded-xl border bg-card">
        {(list.data ?? []).map((p) => (
          <li key={p.id} className={`flex flex-wrap items-center justify-between gap-2 px-4 py-3 ${p.isDeleted || !p.isActive ? "opacity-60" : ""}`} data-testid={`platform-${p.key}`}>
            <div>
              <span className="font-semibold">{p.name}</span> <span className="text-sm text-muted-foreground" dir="ltr">({p.key})</span>
              {p.isDeleted && <span className="ms-2 rounded-md border px-2 py-0.5 text-xs">{t("crud.deleted")}</span>}
              {p.urlTemplate && (
                <p className="text-xs text-muted-foreground" dir="ltr">
                  {p.urlTemplate}
                </p>
              )}
            </div>
            <div className="flex gap-1">
              {!p.isDeleted && (
                <Button variant="ghost" className="h-12 md:h-10" onClick={() => setEditing(p)}>
                  {t("common.edit")}
                </Button>
              )}
              <Button variant="ghost" className={`h-12 md:h-10 ${p.isDeleted ? "" : "text-destructive"}`} onClick={() => toggleDeleted(p)}>
                {p.isDeleted ? t("common.restore") : t("common.delete")}
              </Button>
            </div>
          </li>
        ))}
      </ul>
      {editing && <PlatformEditor platform={editing === "new" ? null : editing} lang={lang} onClose={() => setEditing(null)} onSaved={async () => { setEditing(null); await refresh(); notify.success(t("crud.saved")); }} />}
    </div>
  );
}
