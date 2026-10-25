"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { Plus } from "lucide-react";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { NumberField, Toggle } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import type { CrmSettings, Source, Stage, StageKind } from "./types";

/** Pipeline columns, lead sources, auto hand-out and commission (crm.pipeline.manage). */
export function CrmSettingsPanel() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const stages = useQuery({ queryKey: ["crm-stages"], queryFn: () => api<Stage[]>("/api/v1/crm/stages") });
  const sources = useQuery({ queryKey: ["crm-sources"], queryFn: () => api<Source[]>("/api/v1/crm/sources") });
  const settings = useQuery({ queryKey: ["crm-settings"], gcTime: 0, queryFn: () => api<CrmSettings>("/api/v1/crm/settings") });
  const [s, setS] = useState<CrmSettings | null>(null);
  const v = s ?? settings.data;

  const save = async <T,>(fn: () => Promise<T>, key: string[]) => {
    try {
      await fn();
      notify.success(t("crm.settings.saved"));
      await qc.invalidateQueries({ queryKey: key });
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  return (
    <div className="grid gap-6 lg:grid-cols-2" data-testid="crm-settings">
      <section className="space-y-3 rounded-xl border p-4">
        <h2 className="font-bold">{t("crm.settings.stages")}</h2>
        <p className="text-sm text-muted-foreground">{t("crm.settings.stagesHelp")}</p>
        <ul className="space-y-2">
          {stages.data?.map((x) => (
            <Row key={x.id} name={x.name} order={x.sortOrder} active={x.isActive} badge={t(`crm.settings.kind.${x.kind}`)}
              onSave={(name, order, active) => save(() => api(`/api/v1/crm/stages/${x.id}`, { method: "PUT", body: { name, kind: x.kind, sortOrder: order, isActive: active } }), ["crm-stages"])} />
          ))}
        </ul>
        <AddRow label={t("crm.settings.addStage")} withKind onAdd={(name, kind) => save(() => api("/api/v1/crm/stages", { method: "POST", body: { name, kind, sortOrder: ((stages.data?.at(-1)?.sortOrder ?? 0) + 5), isActive: true } }), ["crm-stages"])} />
      </section>

      <section className="space-y-3 rounded-xl border p-4">
        <h2 className="font-bold">{t("crm.settings.sources")}</h2>
        <ul className="space-y-2">
          {sources.data?.map((x) => (
            <Row key={x.id} name={x.name} order={x.sortOrder} active={x.isActive}
              onSave={(name, order, active) => save(() => api(`/api/v1/crm/sources/${x.id}`, { method: "PUT", body: { name, sortOrder: order, isActive: active } }), ["crm-sources"])} />
          ))}
        </ul>
        <AddRow label={t("crm.settings.addSource")} onAdd={(name) => save(() => api("/api/v1/crm/sources", { method: "POST", body: { name, sortOrder: (sources.data?.at(-1)?.sortOrder ?? 0) + 10, isActive: true } }), ["crm-sources"])} />
      </section>

      {v && (
        <section className="space-y-3 rounded-xl border p-4 lg:col-span-2">
          <h2 className="font-bold">{t("crm.settings.general")}</h2>
          <Toggle label={t("crm.settings.autoAssign")} help={t("crm.settings.autoAssignHelp")} checked={v.autoAssign} onChange={(x) => setS({ ...v, autoAssign: x })} testId="crm-auto-assign" />
          <Toggle label={t("crm.settings.commission")} help={t("crm.settings.commissionHelp")} checked={v.commissionEnabled} onChange={(x) => setS({ ...v, commissionEnabled: x })} />
          {v.commissionEnabled && <div className="w-48"><NumberField label={t("crm.settings.commissionPercent")} help={t("crm.settings.commissionPercentHelp")} value={v.commissionPercent} onChange={(x) => setS({ ...v, commissionPercent: x ?? 0 })} min={0} max={50} /></div>}
          <Button className="h-11" onClick={() => save(() => api("/api/v1/crm/settings", { method: "PUT", body: v }), ["crm-settings"])} data-testid="crm-settings-save">{t("common.save")}</Button>
        </section>
      )}
    </div>
  );
}

function Row({ name, order, active, badge, onSave }: { name: string; order: number; active: boolean; badge?: string; onSave: (name: string, order: number, active: boolean) => void }) {
  const { t } = useI18n();
  const [n, setN] = useState(name);
  const [o, setO] = useState(order);
  const [a, setA] = useState(active);
  const dirty = n !== name || o !== order || a !== active;
  return (
    <li className="flex flex-wrap items-center gap-2">
      <input value={n} onChange={(e) => setN(e.target.value)} aria-label={name} className="h-10 min-w-0 flex-1 rounded-md border bg-background px-2 text-sm" />
      <input type="number" value={o} onChange={(e) => setO(Number(e.target.value))} aria-label={t("crm.settings.order")} className="h-10 w-16 rounded-md border bg-background px-2 text-sm" dir="ltr" />
      {badge && <span className="rounded border px-1.5 text-xs">{badge}</span>}
      <label className="flex items-center gap-1 text-xs"><input type="checkbox" checked={a} onChange={(e) => setA(e.target.checked)} className="size-4" />{t("crm.settings.active")}</label>
      {dirty && <Button className="h-9" onClick={() => onSave(n, o, a)}>{t("common.save")}</Button>}
    </li>
  );
}

function AddRow({ label, withKind, onAdd }: { label: string; withKind?: boolean; onAdd: (name: string, kind: StageKind) => void }) {
  const { t } = useI18n();
  const [n, setN] = useState("");
  const [k, setK] = useState<StageKind>("open");
  return (
    <div className="flex flex-wrap items-center gap-2 border-t pt-3">
      <input value={n} onChange={(e) => setN(e.target.value)} placeholder={label} aria-label={label} className="h-10 min-w-0 flex-1 rounded-md border bg-background px-2 text-sm" />
      {withKind && (
        <select value={k} onChange={(e) => setK(e.target.value as StageKind)} aria-label={label} className="h-10 rounded-md border bg-background px-2 text-sm">
          {(["open", "won", "lost"] as const).map((x) => <option key={x} value={x}>{t(`crm.settings.kind.${x}`)}</option>)}
        </select>
      )}
      <Button variant="outline" className="h-10" disabled={!n.trim()} onClick={() => { onAdd(n.trim(), k); setN(""); }}>
        <Plus aria-hidden="true" />
        {label}
      </Button>
    </div>
  );
}
