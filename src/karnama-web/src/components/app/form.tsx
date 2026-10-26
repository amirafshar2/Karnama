"use client";

import { useId, useState } from "react";
import { Input } from "@/components/ui/input";
import { useI18n } from "@/lib/i18n/provider";
import { formatJalali, parseJalali } from "@/lib/jalali";
import { cn } from "@/lib/utils";

/** Label + control + one plain-language help line (docs/PROMPT2.md rule 0.2). */
export function Field({ label, help, children, className, optional }: { label: string; help?: string; children: (id: string, describedBy?: string) => React.ReactNode; className?: string; optional?: boolean }) {
  const id = useId();
  const helpId = help ? `${id}-help` : undefined;
  const { t } = useI18n();
  return (
    <div className={cn("space-y-2", className)}>
      <label htmlFor={id} className="flex items-baseline gap-2 text-sm font-medium">
        {label}
        {optional && <span className="text-xs font-normal text-muted-foreground">({t("crud.optional")})</span>}
      </label>
      {children(id, helpId)}
      {help && (
        <p id={helpId} className="text-sm [overflow-wrap:anywhere] text-muted-foreground">
          {help}
        </p>
      )}
    </div>
  );
}

export function TextField({ label, help, value, onChange, optional, testId, ...rest }: { label: string; help?: string; value: string; onChange: (v: string) => void; optional?: boolean; testId?: string } & Omit<React.ComponentProps<"input">, "value" | "onChange">) {
  return (
    <Field label={label} help={help} optional={optional}>
      {(id, d) => <Input id={id} aria-describedby={d} className="h-12 md:h-11" value={value} onChange={(e) => onChange(e.target.value)} data-testid={testId} {...rest} />}
    </Field>
  );
}

export function TextArea({ label, help, value, onChange, rows = 3, optional, testId, dir }: { label: string; help?: string; value: string; onChange: (v: string) => void; rows?: number; optional?: boolean; testId?: string; dir?: string }) {
  return (
    <Field label={label} help={help} optional={optional}>
      {(id, d) => (
        <textarea
          id={id}
          aria-describedby={d}
          rows={rows}
          dir={dir}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          data-testid={testId}
          className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base outline-none focus-visible:ring-2 focus-visible:ring-ring md:text-sm"
        />
      )}
    </Field>
  );
}

const toLatin = (s: string) => s.replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0)).replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660));

/** Number input that accepts Persian digits and thousands separators. `null` = empty. */
export function NumberField({ label, help, value, onChange, optional, testId, min, max }: { label: string; help?: string; value: number | null; onChange: (v: number | null) => void; optional?: boolean; testId?: string; min?: number; max?: number }) {
  const { formatNumber } = useI18n();
  const [text, setText] = useState(value === null ? "" : formatNumber(value));
  return (
    <Field label={label} help={help} optional={optional}>
      {(id, d) => (
        <Input
          id={id}
          aria-describedby={d}
          inputMode="numeric"
          dir="ltr"
          className="h-12 text-end md:h-11"
          value={text}
          data-testid={testId}
          onChange={(e) => {
            setText(e.target.value);
            const digits = toLatin(e.target.value).replace(/[^\d-]/g, "");
            onChange(digits === "" ? null : Math.max(min ?? -Infinity, Math.min(max ?? Infinity, Number(digits))));
          }}
          onBlur={() => setText(value === null ? "" : formatNumber(value))}
        />
      )}
    </Field>
  );
}

/** Persian-calendar date typed as ۱۴۰۵/۰۸/۰۳; value is an ISO string (UTC midnight Tehran isn't needed for whole days). */
export function JalaliDateField({ label, help, value, onChange, optional, testId }: { label: string; help?: string; value: string | null; onChange: (iso: string | null) => void; optional?: boolean; testId?: string }) {
  const { formatDate } = useI18n();
  const [text, setText] = useState(formatJalali(value));
  const parsed = text.trim() === "" ? null : parseJalali(text);
  const invalid = text.trim() !== "" && parsed === null;
  return (
    <Field label={label} help={help} optional={optional}>
      {(id, d) => (
        <div className="space-y-1">
          <Input
            id={id}
            aria-describedby={d}
            aria-invalid={invalid}
            inputMode="numeric"
            dir="ltr"
            placeholder="1405/08/03"
            className="h-12 text-end md:h-11"
            value={text}
            data-testid={testId}
            onChange={(e) => {
              setText(e.target.value);
              const iso = e.target.value.trim() === "" ? null : parseJalali(e.target.value);
              if (e.target.value.trim() === "" || iso) onChange(iso ? `${iso}T00:00:00+03:30` : null);
            }}
          />
          {parsed && <p className="text-xs text-muted-foreground">{formatDate(parsed + "T12:00:00", { weekday: "long", day: "numeric", month: "long", year: "numeric" })}</p>}
        </div>
      )}
    </Field>
  );
}

export function SelectField<T extends string>({ label, help, value, onChange, options, testId, optional }: { label: string; help?: string; value: T | ""; onChange: (v: T) => void; options: { value: T; label: string }[]; testId?: string; optional?: boolean }) {
  const { t } = useI18n();
  return (
    <Field label={label} help={help} optional={optional}>
      {(id, d) => (
        <select
          id={id}
          aria-describedby={d}
          value={value}
          onChange={(e) => onChange(e.target.value as T)}
          data-testid={testId}
          className="h-12 w-full rounded-md border border-input bg-card px-3 text-base md:h-11 md:text-sm"
        >
          {optional && <option value="">—</option>}
          {/* Required and still empty: say so, instead of showing the first option as if it were chosen. */}
          {!optional && value === "" && (
            <option value="" disabled>
              {t("common.choose")}
            </option>
          )}
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      )}
    </Field>
  );
}

export function Toggle({ label, help, checked, onChange, testId }: { label: string; help?: string; checked: boolean; onChange: (v: boolean) => void; testId?: string }) {
  const id = useId();
  return (
    <div className="space-y-1">
      <label htmlFor={id} className="flex min-h-12 cursor-pointer items-center gap-3 text-sm font-medium">
        <input id={id} type="checkbox" role="switch" checked={checked} onChange={(e) => onChange(e.target.checked)} className="size-5 accent-[var(--foreground)]" data-testid={testId} aria-describedby={help ? `${id}-help` : undefined} />
        {label}
      </label>
      {help && (
        <p id={`${id}-help`} className="text-sm [overflow-wrap:anywhere] text-muted-foreground">
          {help}
        </p>
      )}
    </div>
  );
}

/** Toman in the UI, rials in the API (money is always a long in rials). */
export const toRial = (toman: number | null) => (toman === null ? null : toman * 10);
export const toToman = (rial: number | null | undefined) => (rial === null || rial === undefined ? null : Math.round(rial / 10));
