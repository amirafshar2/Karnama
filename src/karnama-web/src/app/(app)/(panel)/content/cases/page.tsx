"use client";

import { Plus } from "lucide-react";
import dynamic from "next/dynamic";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faBlog } from "@/lib/i18n/fa-blog";
import { faSiteAdmin } from "@/lib/i18n/fa-siteadmin";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQuery } from "@/lib/query";
import { cn } from "@/lib/utils";
import type { CaseItem } from "@/components/siteadmin/types";

registerTexts(faSiteAdmin);
registerTexts(faBlog);
const CaseEditor = dynamic(() => import("@/components/siteadmin/case-editor").then((m) => m.CaseEditor), { ssr: false });

export default function CasesPage() {
  const allowed = useRequire([{ code: "content.manage", scope: "all" }]);
  const { t, formatNumber } = useI18n();
  const errorMessage = useErrorMessage();
  const list = useQuery({ queryKey: ["cases"], enabled: allowed, queryFn: () => api<CaseItem[]>("/api/v1/cases") });
  const [editing, setEditing] = useState<CaseItem | "new" | null>(null);
  if (!allowed) return null;

  const publish = async (c: CaseItem, on: boolean) => {
    try {
      await api(`/api/v1/cases/${c.id}/publish`, { method: "POST", body: { published: on } });
      await list.refetch();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  const remove = async (c: CaseItem) => {
    try {
      await api(`/api/v1/cases/${c.id}`, { method: "DELETE" });
      notify.success(t("cases.deleted"));
      await list.refetch();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-extrabold">{t("cases.title")}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t("cases.subtitle")}</p>
        </div>
        <Button className="h-12" onClick={() => setEditing("new")} data-testid="case-new">
          <Plus aria-hidden="true" />
          {t("cases.new")}
        </Button>
      </header>
      {list.data?.length === 0 && <p className="rounded-xl border border-dashed p-10 text-center text-muted-foreground">{t("cases.empty")}</p>}
      <ul className="grid gap-4 md:grid-cols-2" data-testid="cases-admin">
        {(list.data ?? []).map((c) => (
          <li key={c.id} className="space-y-3 rounded-xl border bg-card p-4">
            <div className="flex flex-wrap items-center gap-2">
              <button type="button" onClick={() => setEditing(c)} className="text-start text-lg font-bold hover:underline">{c.businessName}</button>
              <span className={cn("rounded-full px-2 py-0.5 text-xs font-semibold", c.published ? "bg-emerald-100 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-200" : "bg-muted")}>
                {c.published ? t("cases.published") : t("cases.draft")}
              </span>
              {c.isSample && <span className="rounded-full bg-muted px-2 py-0.5 text-xs">{t("cases.sample")}</span>}
            </div>
            <p className="text-sm text-muted-foreground">
              {[c.industry, c.city].filter(Boolean).join("، ")}
              {c.followersBefore !== null && c.followersAfter !== null && ` · ${formatNumber(c.followersBefore)} → ${formatNumber(c.followersAfter)}`}
            </p>
            {!c.consentObtained && !c.isSample && <p className="text-xs font-semibold text-amber-700 dark:text-amber-400">{t("cases.consentHelp")}</p>}
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" className="h-10" onClick={() => setEditing(c)}>{t("common.edit")}</Button>
              <Button variant={c.published ? "ghost" : "default"} className="h-10" onClick={() => void publish(c, !c.published)} data-testid={`case-publish-${c.slug}`}>
                {c.published ? t("cases.unpublish") : t("cases.publish")}
              </Button>
              <Button variant="ghost" className="h-10 text-destructive" onClick={() => void remove(c)}>{t("common.delete")}</Button>
            </div>
          </li>
        ))}
      </ul>
      {editing && (
        <CaseEditor
          item={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            void list.refetch();
          }}
        />
      )}
    </div>
  );
}
