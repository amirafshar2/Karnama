"use client";

import { useQuery } from "@/lib/query";
import { Plus, X } from "lucide-react";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { NumberField, Toggle } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faN4 } from "@/lib/i18n/fa-n4";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";

registerTexts(faN4);

type Settings = { daysBefore: number[]; onEndDay: boolean; quotaUsedUp: boolean; contractEndPasswords: boolean };

/** PROMPT2 §10.3: which days before the end the client and the managers are reminded (editable, not hard-coded). */
export function NoticeSettings({ onClose }: { onClose: () => void }) {
  const { t } = useI18n();
  const current = useQuery({ queryKey: ["package-notices"], queryFn: () => api<Settings>("/api/v1/settings/package-notices"), gcTime: 0 });
  if (!current.data) return <EditorSheet title={t("notices.title")} onClose={onClose} onSubmit={() => {}} busy={false} error={current.error} loading={!current.error}>{null}</EditorSheet>;
  return <Form initial={current.data} onClose={onClose} />;
}

function Form({ initial, onClose }: { initial: Settings; onClose: () => void }) {
  const { t, formatNumber } = useI18n();
  const [s, setS] = useState(initial);
  const [extra, setExtra] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const add = () => {
    if (extra === null || extra < 1 || extra > 60 || s.daysBefore.includes(extra)) return;
    setS({ ...s, daysBefore: [...s.daysBefore, extra].sort((a, b) => b - a) });
    setExtra(null);
  };

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await api("/api/v1/settings/package-notices", { method: "PUT", body: s });
      notify.success(t("crud.saved"));
      onClose();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  return (
    <EditorSheet title={t("notices.title")} description={t("notices.subtitle")} onClose={onClose} onSubmit={submit} busy={busy} error={error} testId="notices">
      <fieldset className="space-y-3">
        <legend className="font-bold">{t("notices.days")}</legend>
        <ul className="flex flex-wrap gap-2" data-testid="notices-days">
          {s.daysBefore.map((d) => (
            <li key={d} className="flex items-center gap-1 rounded-full border bg-secondary py-1 ps-3 pe-1 text-sm text-secondary-foreground">
              {t("notices.dayLabel", { days: formatNumber(d) })}
              <Button type="button" variant="ghost" size="icon" className="size-8 rounded-full" aria-label={`${t("crud.remove")} ${t("notices.dayLabel", { days: formatNumber(d) })}`} onClick={() => setS({ ...s, daysBefore: s.daysBefore.filter((x) => x !== d) })}>
                <X className="size-4" aria-hidden="true" />
              </Button>
            </li>
          ))}
        </ul>
        {s.daysBefore.length < 6 && (
          <div className="flex items-end gap-2">
            <div className="w-40">
              <NumberField label={t("notices.addDay")} help={t("notices.daysHelp")} value={extra} onChange={setExtra} min={1} max={60} optional testId="notices-day" />
            </div>
            <Button type="button" variant="outline" className="mb-7 h-12 md:h-11" onClick={add} disabled={extra === null} data-testid="notices-day-add">
              <Plus aria-hidden="true" />
              {t("notices.addDay")}
            </Button>
          </div>
        )}
      </fieldset>
      <Toggle label={t("notices.onEndDay")} checked={s.onEndDay} onChange={(v) => setS({ ...s, onEndDay: v })} testId="notices-endday" />
      <Toggle label={t("notices.quota")} checked={s.quotaUsedUp} onChange={(v) => setS({ ...s, quotaUsedUp: v })} />
      <Toggle label={t("notices.contractEnd")} checked={s.contractEndPasswords} onChange={(v) => setS({ ...s, contractEndPasswords: v })} />
    </EditorSheet>
  );
}
