"use client";

import { JalaliDateField, NumberField, SelectField, TextArea, TextField } from "@/components/app/form";
import { useI18n } from "@/lib/i18n/provider";

export type FormFieldDef = {
  key: string;
  type: string;
  label: Record<string, string>;
  required: boolean;
  help?: Record<string, string> | null;
  min?: number | null;
  max?: number | null;
  options?: { value: string; label: Record<string, string> }[] | null;
};

const pick = (m: Record<string, string> | null | undefined, lang: string) => (m ? (m[lang] ?? m.fa ?? Object.values(m)[0] ?? "") : "");

/** Renders a service's order form (built in the form builder) and keeps the answers as plain JSON. */
export function DynamicForm({ fields, value, onChange, fileHelp }: { fields: FormFieldDef[]; value: Record<string, unknown>; onChange: (v: Record<string, unknown>) => void; fileHelp: string }) {
  const { lang } = useI18n();
  const set = (k: string, v: unknown) => onChange({ ...value, [k]: v });
  return (
    <div className="space-y-5">
      {fields.map((f) => {
        const label = pick(f.label, lang) + (f.required ? " *" : "");
        const help = pick(f.help, lang) || undefined;
        const testId = `answer-${f.key}`;
        switch (f.type) {
          case "textarea":
            return <TextArea key={f.key} label={label} help={help} value={String(value[f.key] ?? "")} onChange={(v) => set(f.key, v)} rows={4} testId={testId} />;
          case "number":
            return <NumberField key={f.key} label={label} help={help} value={(value[f.key] as number | undefined) ?? null} onChange={(v) => set(f.key, v)} min={f.min ?? undefined} max={f.max ?? undefined} testId={testId} />;
          case "date":
          case "datetime":
            return <JalaliDateField key={f.key} label={label} help={help} value={(value[f.key] as string | undefined) ?? null} onChange={(v) => set(f.key, v)} testId={testId} />;
          case "select":
            return <SelectField key={f.key} label={label} help={help} value={String(value[f.key] ?? "")} onChange={(v) => set(f.key, v || null)} optional options={(f.options ?? []).map((o) => ({ value: o.value, label: pick(o.label, lang) }))} testId={testId} />;
          case "multiselect": {
            const picked = (value[f.key] as string[] | undefined) ?? [];
            return (
              <fieldset key={f.key} className="space-y-2">
                <legend className="text-sm font-medium">{label}</legend>
                <div className="flex flex-wrap gap-2">
                  {(f.options ?? []).map((o) => (
                    <label key={o.value} className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border px-3 text-sm">
                      <input type="checkbox" className="size-5" checked={picked.includes(o.value)} onChange={(e) => set(f.key, e.target.checked ? [...picked, o.value] : picked.filter((x) => x !== o.value))} />
                      {pick(o.label, lang)}
                    </label>
                  ))}
                </div>
                {help && <p className="text-sm text-muted-foreground">{help}</p>}
              </fieldset>
            );
          }
          case "file":
            return (
              <div key={f.key} className="space-y-1">
                <p className="text-sm font-medium">{pick(f.label, lang)}</p>
                <p className="text-sm text-muted-foreground">{fileHelp}</p>
              </div>
            );
          case "color":
            return (
              <TextField key={f.key} label={label} help={help} value={String(value[f.key] ?? "")} onChange={(v) => set(f.key, v)} type="color" className="h-12 w-24 p-1" testId={testId} />
            );
          default:
            return <TextField key={f.key} label={label} help={help} value={String(value[f.key] ?? "")} onChange={(v) => set(f.key, v)} dir={f.type === "url" ? "ltr" : undefined} type={f.type === "url" ? "url" : "text"} testId={testId} />;
        }
      })}
    </div>
  );
}
