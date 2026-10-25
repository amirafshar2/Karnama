"use client";

import { useQuery } from "@/lib/query";
import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { NumberField, SelectField, TextField, Toggle, toRial, toToman } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import type { KeysetPage, RoleListItem } from "@/lib/types";
import { faM2Forms } from "@/lib/i18n/fa-m2-forms";
import { registerTexts as registerFormTexts } from "@/lib/i18n/registry";

registerFormTexts(faM2Forms);

type L = Record<string, string>;
type FormField = { key: string; type: string; label: L; required: boolean; help?: L | null; min?: number | null; max?: number | null; options?: { value: string; label: L }[] | null };
type Stage = { id: string | null; name: L; roleId: string | null; estimatedMinutes: number; slaHours: number | null; needsAdminReview: boolean; needsClientApproval: boolean };
type Service = { id: string; nameValues: L; description: string | null; icon: string | null; basePrice: number | null; consumesQuota: boolean; weight: number; sortOrder: number; isActive: boolean; form: FormField[]; stages: Stage[]; version: number };

const TYPES = ["text", "textarea", "number", "date", "datetime", "select", "multiselect", "file", "url", "platform", "color"] as const;

function move<T>(list: T[], i: number, d: number) {
  const j = i + d;
  if (j < 0 || j >= list.length) return list;
  const c = [...list];
  [c[i], c[j]] = [c[j], c[i]];
  return c;
}

const EMPTY: Service = { id: "", nameValues: {}, description: null, icon: null, basePrice: null, consumesQuota: true, weight: 1, sortOrder: 100, isActive: true, form: [], stages: [], version: 0 };

type Props = { id: string; onClose: () => void; onSaved: (created: boolean) => void };

export function ServiceEditor(props: Props) {
  const { t } = useI18n();
  const isNew = props.id === "new";
  const existing = useQuery({ queryKey: ["service", props.id], enabled: !isNew, queryFn: () => api<Service>(`/api/v1/services/${props.id}`), gcTime: 0 });
  if (!isNew && !existing.data) return <EditorSheet title={t("services.formEdit")} onClose={props.onClose} onSubmit={() => {}} busy={false} error={existing.error} loading={!existing.error} wide>{null}</EditorSheet>;
  return <ServiceForm {...props} initial={existing.data ?? EMPTY} />;
}

function ServiceForm({ id, onClose, onSaved, initial }: Props & { initial: Service }) {
  const { t, lang } = useI18n();
  const isNew = id === "new";
  const roles = useQuery({ queryKey: ["roles", "options"], queryFn: () => api<KeysetPage<RoleListItem>>("/api/v1/roles?limit=100") });
  const [s, setS] = useState<Service | null>(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const set = (patch: Partial<Service>) => setS((x) => (x ? { ...x, ...patch } : x));
  const setField = (i: number, patch: Partial<FormField>) => set({ form: s!.form.map((f, j) => (j === i ? { ...f, ...patch } : f)) });
  const setStage = (i: number, patch: Partial<Stage>) => set({ stages: s!.stages.map((f, j) => (j === i ? { ...f, ...patch } : f)) });

  const submit = async () => {
    if (!s) return;
    setBusy(true);
    setError(null);
    try {
      const body = {
        name: s.nameValues,
        description: s.description,
        icon: s.icon,
        basePrice: s.basePrice,
        consumesQuota: s.consumesQuota,
        weight: s.weight,
        sortOrder: s.sortOrder,
        isActive: s.isActive,
        form: s.form,
        stages: s.stages,
        version: isNew ? null : s.version,
      };
      await api(isNew ? "/api/v1/services" : `/api/v1/services/${id}`, { method: isNew ? "POST" : "PUT", body });
      onSaved(isNew);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const roleOptions = (roles.data?.items ?? []).filter((r) => r.systemKey !== "client").map((r) => ({ value: r.id, label: r.name }));

  return (
    <EditorSheet title={isNew ? t("services.formCreate") : t("services.formEdit")} onClose={onClose} onSubmit={submit} busy={busy} error={error} loading={!s} wide testId="service-editor">
      {s && (
        <>
          <div className="grid gap-5 sm:grid-cols-2">
            <TextField label={t("services.name")} help={t("services.nameHelp")} value={s.nameValues[lang] ?? ""} onChange={(v) => set({ nameValues: { ...s.nameValues, [lang]: v } })} testId="service-name" />
            <NumberField label={t("services.basePrice")} help={t("services.basePriceHelp")} value={toToman(s.basePrice)} onChange={(v) => set({ basePrice: toRial(v) })} optional min={0} />
            <NumberField label={t("services.weight")} help={t("services.weightHelp")} value={s.weight} onChange={(v) => set({ weight: v ?? 1 })} min={1} max={100} />
            <NumberField label={t("crud.sortOrder")} help={t("crud.sortOrderHelp")} value={s.sortOrder} onChange={(v) => set({ sortOrder: v ?? 0 })} />
          </div>
          <Toggle label={t("services.consumes")} help={t("services.consumesHelp")} checked={s.consumesQuota} onChange={(v) => set({ consumesQuota: v })} />
          <Toggle label={t("users.active")} checked={s.isActive} onChange={(v) => set({ isActive: v })} />

          <section className="space-y-3" aria-labelledby="stages-h">
            <h3 id="stages-h" className="text-lg font-bold">
              {t("services.stages")}
            </h3>
            <p className="text-sm text-muted-foreground">{t("services.stagesHelp")}</p>
            <ol className="space-y-3">
              {s.stages.map((st, i) => (
                <li key={st.id ?? `n${i}`} className="space-y-3 rounded-xl border p-4" data-testid={`stage-${i}`}>
                  <div className="flex items-center justify-between gap-2">
                    <span className="grid size-8 place-items-center rounded-full bg-secondary text-sm font-bold">{i + 1}</span>
                    <RowTools onUp={() => set({ stages: move(s.stages, i, -1) })} onDown={() => set({ stages: move(s.stages, i, 1) })} onRemove={() => set({ stages: s.stages.filter((_, j) => j !== i) })} />
                  </div>
                  <div className="grid gap-4 sm:grid-cols-2">
                    <TextField label={t("services.stageName")} value={st.name[lang] ?? ""} onChange={(v) => setStage(i, { name: { ...st.name, [lang]: v } })} testId={`stage-name-${i}`} />
                    <SelectField label={t("services.stageRole")} help={t("services.stageRoleHelp")} value={st.roleId ?? ""} onChange={(v) => setStage(i, { roleId: v || null })} options={roleOptions} optional />
                    <NumberField label={t("services.stageMinutes")} help={t("services.stageMinutesHelp")} value={st.estimatedMinutes} onChange={(v) => setStage(i, { estimatedMinutes: v ?? 0 })} min={0} />
                    <NumberField label={t("services.stageSla")} value={st.slaHours} onChange={(v) => setStage(i, { slaHours: v })} optional min={1} />
                  </div>
                  <div className="flex flex-wrap gap-x-6">
                    <Toggle label={t("services.stageReview")} checked={st.needsAdminReview} onChange={(v) => setStage(i, { needsAdminReview: v })} />
                    <Toggle label={t("services.stageClient")} checked={st.needsClientApproval} onChange={(v) => setStage(i, { needsClientApproval: v })} />
                  </div>
                </li>
              ))}
            </ol>
            <Button type="button" variant="outline" className="h-12 md:h-11" onClick={() => set({ stages: [...s.stages, { id: null, name: {}, roleId: null, estimatedMinutes: 60, slaHours: null, needsAdminReview: true, needsClientApproval: false }] })} data-testid="stage-add">
              <Plus aria-hidden="true" />
              {t("services.addStage")}
            </Button>
          </section>

          <section className="space-y-3" aria-labelledby="form-h">
            <h3 id="form-h" className="text-lg font-bold">
              {t("services.form")}
            </h3>
            <p className="text-sm text-muted-foreground">{t("services.formHelp")}</p>
            <ul className="space-y-3">
              {s.form.map((f, i) => (
                <li key={i} className="space-y-3 rounded-xl border p-4" data-testid={`field-${i}`}>
                  <div className="flex justify-end">
                    <RowTools onUp={() => set({ form: move(s.form, i, -1) })} onDown={() => set({ form: move(s.form, i, 1) })} onRemove={() => set({ form: s.form.filter((_, j) => j !== i) })} />
                  </div>
                  <div className="grid gap-4 sm:grid-cols-3">
                    <TextField label={t("services.fieldLabel")} value={f.label[lang] ?? ""} onChange={(v) => setField(i, { label: { ...f.label, [lang]: v } })} testId={`field-label-${i}`} />
                    <TextField label={t("services.fieldKey")} help={t("services.fieldKeyHelp")} value={f.key} onChange={(v) => setField(i, { key: v })} dir="ltr" testId={`field-key-${i}`} />
                    <SelectField label={t("services.fieldType")} value={f.type} onChange={(v) => setField(i, { type: v })} options={TYPES.map((x) => ({ value: x, label: t(`services.type.${x}` as TextKey) }))} />
                  </div>
                  {(f.type === "select" || f.type === "multiselect") && (
                    <OptionsEditor value={f.options ?? []} lang={lang} onChange={(options) => setField(i, { options })} label={t("services.fieldOptions")} />
                  )}
                  <Toggle label={t("services.fieldRequired")} checked={f.required} onChange={(v) => setField(i, { required: v })} />
                </li>
              ))}
            </ul>
            <Button type="button" variant="outline" className="h-12 md:h-11" onClick={() => set({ form: [...s.form, { key: `field${s.form.length + 1}`, type: "text", label: {}, required: false }] })} data-testid="field-add">
              <Plus aria-hidden="true" />
              {t("services.addField")}
            </Button>
          </section>
        </>
      )}
    </EditorSheet>
  );
}

function OptionsEditor({ value, onChange, lang, label }: { value: { value: string; label: L }[]; onChange: (v: { value: string; label: L }[]) => void; lang: string; label: string }) {
  const [text, setText] = useState(value.map((o) => o.label[lang] ?? o.value).join("\n"));
  return (
    <label className="block space-y-2 text-sm font-medium">
      {label}
      <textarea
        rows={4}
        className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base md:text-sm"
        value={text}
        onChange={(e) => {
          setText(e.target.value);
          const lines = e.target.value.split("\n").map((l) => l.trim()).filter(Boolean);
          onChange(lines.map((l, i) => ({ value: value[i]?.value ?? `o${i + 1}`, label: { ...(value[i]?.label ?? {}), [lang]: l } })));
        }}
      />
    </label>
  );
}

export function RowTools({ onUp, onDown, onRemove }: { onUp: () => void; onDown: () => void; onRemove: () => void }) {
  const { t } = useI18n();
  return (
    <div className="flex gap-1">
      <Button type="button" variant="ghost" size="icon" className="size-11" onClick={onUp} aria-label={t("crud.moveUp")}>
        <ArrowUp aria-hidden="true" />
      </Button>
      <Button type="button" variant="ghost" size="icon" className="size-11" onClick={onDown} aria-label={t("crud.moveDown")}>
        <ArrowDown aria-hidden="true" />
      </Button>
      <Button type="button" variant="ghost" size="icon" className="size-11 text-destructive" onClick={onRemove} aria-label={t("crud.remove")}>
        <Trash2 aria-hidden="true" />
      </Button>
    </div>
  );
}
