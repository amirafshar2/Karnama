"use client";

import { Plus } from "lucide-react";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { SelectField, TextField, Toggle } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import { useQuery, useQueryClient } from "@/lib/query";

type Dir = "rtl" | "ltr";
type Cal = "persian" | "gregorian" | "hijri";
type Digits = "persian" | "latin";
type Lang = { code: string; name: string; direction: Dir; calendar: Cal; digits: Digits; isDefault: boolean; isActive: boolean; sortOrder: number };
const EMPTY: Lang = { code: "", name: "", direction: "ltr", calendar: "gregorian", digits: "latin", isDefault: false, isActive: true, sortOrder: 10 };

/** Languages of the interface (PROMPT R13): add one (e.g. English, LTR), choose direction, calendar and digits, switch it on or off. */
export function LanguagesCard() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const list = useQuery({ queryKey: ["languages-admin"], queryFn: () => api<Lang[]>("/api/v1/i18n/languages/admin") });
  const [form, setForm] = useState<Lang | null>(null);
  const [isNew, setIsNew] = useState(false);
  const [busy, setBusy] = useState(false);

  const save = async () => {
    if (!form) return;
    setBusy(true);
    try {
      const body = { code: form.code, name: form.name, direction: form.direction, calendar: form.calendar, digits: form.digits, isActive: form.isActive, sortOrder: form.sortOrder };
      await api(isNew ? "/api/v1/i18n/languages/admin" : `/api/v1/i18n/languages/admin/${form.code}`, { method: isNew ? "POST" : "PUT", body });
      notify.success(t("langs.saved"));
      setForm(null);
      await qc.invalidateQueries({ queryKey: ["languages-admin"] });
      window.dispatchEvent(new Event("karnama:texts-changed"));
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="space-y-4 rounded-xl border bg-card p-5" data-testid="languages-card">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-lg font-bold">{t("langs.title")}</h2>
          <p className="text-sm text-muted-foreground">{t("langs.help")}</p>
        </div>
        <Button type="button" variant="outline" className="h-11" onClick={() => { setForm({ ...EMPTY }); setIsNew(true); }} data-testid="lang-add">
          <Plus aria-hidden="true" />
          {t("langs.add")}
        </Button>
      </div>
      <ul className="flex flex-wrap gap-2">
        {(list.data ?? []).map((l) => (
          <li key={l.code}>
            <button type="button" onClick={() => { setForm({ ...l }); setIsNew(false); }} className="flex min-h-11 items-center gap-2 rounded-full border px-4 text-sm hover:bg-muted">
              <span className="font-bold">{l.name}</span>
              <code dir="ltr" className="text-xs text-muted-foreground">{l.code} · {l.direction.toUpperCase()}</code>
              {!l.isActive && <span className="text-xs text-muted-foreground">({t("langs.off")})</span>}
            </button>
          </li>
        ))}
      </ul>
      {form && (
        <div className="grid gap-4 rounded-lg border p-4 sm:grid-cols-2">
          {isNew && <TextField label={t("langs.code")} help={t("langs.codeHelp")} value={form.code} onChange={(v) => setForm({ ...form, code: v })} dir="ltr" testId="lang-code" />}
          <TextField label={t("langs.name")} help={t("langs.nameHelp")} value={form.name} onChange={(v) => setForm({ ...form, name: v })} testId="lang-name" />
          <SelectField label={t("langs.direction")} help={t("langs.directionHelp")} value={form.direction} onChange={(v) => setForm({ ...form, direction: v })}
            options={[{ value: "rtl", label: t("langs.rtl") }, { value: "ltr", label: t("langs.ltr") }]} testId="lang-dir" />
          <SelectField label={t("langs.calendar")} help={t("langs.calendarHelp")} value={form.calendar} onChange={(v) => setForm({ ...form, calendar: v })}
            options={[{ value: "persian", label: t("langs.persian") }, { value: "gregorian", label: t("langs.gregorian") }, { value: "hijri", label: t("langs.hijri") }]} />
          <SelectField label={t("langs.digits")} help={t("langs.digitsHelp")} value={form.digits} onChange={(v) => setForm({ ...form, digits: v })}
            options={[{ value: "persian", label: t("langs.digitsFa") }, { value: "latin", label: t("langs.digitsLatin") }]} />
          <Toggle label={t("langs.active")} help={t("langs.activeHelp")} checked={form.isActive} onChange={(v) => setForm({ ...form, isActive: v })} />
          <div className="flex gap-2 sm:col-span-2">
            <Button type="button" className="h-11" disabled={busy} onClick={() => void save()} data-testid="lang-save">{t("langs.save")}</Button>
            <Button type="button" variant="ghost" className="h-11" onClick={() => setForm(null)}>{t("langs.cancel")}</Button>
          </div>
        </div>
      )}
    </section>
  );
}
