/**
 * German version: the app uses the Gregorian calendar. The old Jalali helpers keep their names but work on
 * Gregorian dates, so the pages that used them need no other change.
 */
const pad = (n: number) => String(n).padStart(2, "0");

/** Identity mapping (year, month, day) with overflow normalised by Date. */
export function toGregorian(y: number, m: number, d: number) {
  const dt = new Date(Date.UTC(y, m - 1, d));
  return { gy: dt.getUTCFullYear(), gm: dt.getUTCMonth() + 1, gd: dt.getUTCDate() };
}

export function toJalali(gy: number, gm: number, gd: number) {
  return { jy: gy, jm: gm, jd: gd };
}

/** "25.10.2026", "25.10.26" or "2026-10-25" → ISO date "2026-10-25", or null. */
export function parseJalali(text: string): string | null {
  const latin = text.replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0)).replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660)).trim();
  let y: number, m: number, d: number;
  let r = /^(\d{1,2})[./-](\d{1,2})[./-](\d{2}|\d{4})$/.exec(latin);
  if (r) { d = Number(r[1]); m = Number(r[2]); y = Number(r[3]); if (y < 100) y += 2000; }
  else {
    r = /^(\d{4})[./-](\d{1,2})[./-](\d{1,2})$/.exec(latin);
    if (!r) return null;
    y = Number(r[1]); m = Number(r[2]); d = Number(r[3]);
  }
  if (m < 1 || m > 12 || d < 1 || d > 31 || y < 1900 || y > 2200) return null;
  const g = toGregorian(y, m, d);
  if (g.gy !== y || g.gm !== m || g.gd !== d) return null; // e.g. 31.02.
  return `${y}-${pad(m)}-${pad(d)}`;
}

/** ISO date/time → "25.10.2026". */
export function formatJalali(iso: string | null | undefined): string {
  if (!iso) return "";
  const d = new Date(iso);
  return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`;
}
