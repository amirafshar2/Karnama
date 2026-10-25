"use client";

import { Star } from "lucide-react";
import { useI18n } from "@/lib/i18n/provider";
import { cn } from "@/lib/utils";

// ⚠ TEMPORARY STAND-IN: no matching rating input found on 21st.dev yet (see docs/21st-components.md).
/** 1–5 stars as a native radio group: keyboard arrows work, screen readers say "4 of 5". */
export function StarRating({ value, onChange, label, testId, disabled }: { value: number | null; onChange: (v: number) => void; label: string; testId?: string; disabled?: boolean }) {
  const { t, formatNumber } = useI18n();
  return (
    <fieldset className="space-y-1" data-testid={testId} disabled={disabled}>
      <legend className="text-sm font-medium">{label}</legend>
      <div className="flex gap-1" dir="ltr">
        {[1, 2, 3, 4, 5].map((n) => (
          <label key={n} className="grid size-11 cursor-pointer place-items-center rounded-md focus-within:ring-2 focus-within:ring-ring">
            <input type="radio" className="sr-only" name={testId ?? label} value={n} checked={value === n} onChange={() => onChange(n)} data-testid={testId ? `${testId}-${n}` : undefined} />
            <Star aria-hidden="true" className={cn("size-7 transition-colors", value !== null && n <= value ? "fill-amber-400 text-amber-500" : "text-muted-foreground")} />
            <span className="sr-only">{t("stars.of", { n: formatNumber(n) })}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/** Read-only stars for tables. */
export function StarsShown({ value }: { value: number | null | undefined }) {
  const { t, formatNumber } = useI18n();
  if (!value) return <span className="text-muted-foreground">—</span>;
  return (
    <span className="inline-flex items-center gap-0.5" dir="ltr" aria-label={t("stars.of", { n: formatNumber(value) })}>
      {[1, 2, 3, 4, 5].map((n) => (
        <Star key={n} aria-hidden="true" className={cn("size-3.5", n <= value ? "fill-amber-400 text-amber-500" : "text-muted-foreground/40")} />
      ))}
    </span>
  );
}
