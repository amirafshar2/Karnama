"use client";

import { JalaliDateField } from "@/components/app/form";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { toGregorian, toJalali } from "@/lib/jalali";

const iso = (gy: number, gm: number, gd: number) => `${gy}-${String(gm).padStart(2, "0")}-${String(gd).padStart(2, "0")}T12:00:00+03:30`;
const days = (n: number) => new Date(Date.now() - n * 86_400_000).toISOString();

/** Persian-month boundaries in Gregorian ISO, so the server never has to guess the calendar. */
export function monthRange(offset: number): [string, string] {
  const now = new Date();
  const j = toJalali(now.getFullYear(), now.getMonth() + 1, now.getDate());
  let jy = j.jy, jm = j.jm + offset;
  while (jm < 1) { jm += 12; jy -= 1; }
  const start = toGregorian(jy, jm, 1);
  if (offset === 0) return [iso(start.gy, start.gm, start.gd), now.toISOString()];
  const ny = jm === 12 ? jy + 1 : jy, nm = jm === 12 ? 1 : jm + 1;
  const next = toGregorian(ny, nm, 1);
  const end = new Date(Date.UTC(next.gy, next.gm - 1, next.gd) - 86_400_000);
  return [iso(start.gy, start.gm, start.gd), iso(end.getUTCFullYear(), end.getUTCMonth() + 1, end.getUTCDate())];
}

export function PeriodPicker({ from, to, onChange }: { from: string | null; to: string | null; onChange: (from: string | null, to: string | null) => void }) {
  const { t } = useI18n();
  const shortcuts: [TextKey, () => [string, string]][] = [
    ["rpt.thisMonth", () => monthRange(0)],
    ["rpt.lastMonth", () => monthRange(-1)],
    ["rpt.last7", () => [days(6), new Date().toISOString()]],
    ["rpt.last30", () => [days(29), new Date().toISOString()]],
  ];
  return (
    <fieldset className="flex flex-wrap items-end gap-3" data-print-hide>
      <legend className="sr-only">{t("rpt.period")}</legend>
      <div className="w-44">
        <JalaliDateField label={t("rpt.from")} value={from} onChange={(v) => onChange(v, to)} />
      </div>
      <div className="w-44">
        <JalaliDateField label={t("rpt.to")} value={to} onChange={(v) => onChange(from, v)} />
      </div>
      <div className="flex flex-wrap gap-1.5 pb-0.5">
        {shortcuts.map(([k, f]) => (
          <button key={k} type="button" className="min-h-11 rounded-md border px-3 text-sm hover:bg-muted" onClick={() => onChange(...f())} data-testid={`period-${k.split(".")[1]}`}>
            {t(k)}
          </button>
        ))}
      </div>
    </fieldset>
  );
}
