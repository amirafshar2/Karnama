"use client";

import { Phone } from "lucide-react";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faSiteAdmin } from "@/lib/i18n/fa-siteadmin";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useKeysetList } from "@/lib/use-keyset";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";
import type { Lead, LeadStatus } from "@/components/siteadmin/types";

registerTexts(faSiteAdmin);
const STATUSES: LeadStatus[] = ["new", "contacted", "won", "lost"];

export default function LeadsPage() {
  const allowed = useRequire([{ code: "leads.manage", scope: "all" }]);
  const { t, formatDate } = useI18n();
  const [status, setStatus] = useState<LeadStatus | "">("");
  const list = useKeysetList<Lead>("leads", "/api/v1/leads", { status: status || undefined, limit: "50" }, allowed);
  useLive(["notification"], [["leads"]]);
  if (!allowed) return null;
  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-extrabold">{t("leads.title")}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t("leads.subtitle")}</p>
      </header>
      <div className="flex flex-wrap gap-2" role="tablist">
        {(["", ...STATUSES] as (LeadStatus | "")[]).map((s) => (
          <button key={s || "all"} role="tab" aria-selected={status === s} onClick={() => setStatus(s)}
            className={cn("min-h-10 rounded-full border-2 px-4 text-sm font-semibold", status === s ? "border-foreground bg-foreground text-background" : "border-border hover:border-foreground")}>
            {s ? t(`leads.status.${s}` as TextKey) : t("blog.filter.all" as TextKey)}
          </button>
        ))}
      </div>
      {!list.isLoading && list.items.length === 0 && <p className="rounded-xl border border-dashed p-10 text-center text-muted-foreground">{t("leads.empty")}</p>}
      <ul className="space-y-3" data-testid="leads">
        {list.items.map((l) => (
          <LeadRow key={l.id} lead={l} onSaved={() => void list.refresh()} formatDate={formatDate} />
        ))}
      </ul>
      {list.hasMore && <Button variant="outline" onClick={list.loadMore}>{t("common.loadMore")}</Button>}
    </div>
  );
}

function LeadRow({ lead, onSaved, formatDate }: { lead: Lead; onSaved: () => void; formatDate: (d: string, o?: Intl.DateTimeFormatOptions) => string }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [status, setStatus] = useState(lead.status);
  const [note, setNote] = useState(lead.note ?? "");
  const save = async () => {
    try {
      await api(`/api/v1/leads/${lead.id}`, { method: "PUT", body: { status, note } });
      notify.success(t("leads.saved"));
      onSaved();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  return (
    <li className={cn("space-y-3 rounded-xl border bg-card p-4", lead.status === "new" && "border-2 border-foreground")}>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
        <span className="text-lg font-bold">{lead.name}</span>
        {lead.business && <span className="text-sm text-muted-foreground">{lead.business}</span>}
        <span className="ms-auto text-xs text-muted-foreground">{formatDate(lead.createdAt, { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" })}</span>
      </div>
      {lead.message && <p className="whitespace-pre-wrap text-sm leading-7">{lead.message}</p>}
      <div className="flex flex-wrap items-center gap-2">
        <Button asChild variant="outline" className="h-11">
          <a href={`tel:${lead.phone}`} dir="ltr">
            <Phone aria-hidden="true" />
            {lead.phone}
          </a>
        </Button>
        <select value={status} onChange={(e) => setStatus(e.target.value as LeadStatus)} aria-label={t("leads.title")} className="h-11 rounded-md border bg-transparent px-2">
          {STATUSES.map((s) => <option key={s} value={s}>{t(`leads.status.${s}` as TextKey)}</option>)}
        </select>
        <input value={note} onChange={(e) => setNote(e.target.value)} placeholder={t("leads.note")} aria-label={t("leads.note")} className="h-11 min-w-0 flex-1 rounded-md border bg-transparent px-3" />
        <Button className="h-11" onClick={() => void save()}>{t("leads.save")}</Button>
      </div>
    </li>
  );
}
