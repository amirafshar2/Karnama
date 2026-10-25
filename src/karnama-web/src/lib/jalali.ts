/**
 * Jalali (Persian) ↔ Gregorian conversion (Borkowski's algorithm, as in jalaali-js, MIT). No dependencies.
 * Display of dates uses Intl (calendar=persian); this is for parsing what people type.
 */
const breaks = [-61, 9, 38, 199, 426, 686, 756, 818, 1111, 1181, 1210, 1635, 2060, 2097, 2192, 2262, 2324, 2394, 2456, 3178];
const div = (a: number, b: number) => ~~(a / b);
const mod = (a: number, b: number) => a - ~~(a / b) * b;

function jalCal(jy: number) {
  let jp = breaks[0];
  let jump = 0;
  let leapJ = -14;
  const gy = jy + 621;
  if (jy < jp || jy >= breaks[breaks.length - 1]) throw new Error("out of range");
  for (let i = 1; i < breaks.length; i++) {
    const jm = breaks[i];
    jump = jm - jp;
    if (jy < jm) break;
    leapJ = leapJ + div(jump, 33) * 8 + div(mod(jump, 33), 4);
    jp = jm;
  }
  let n = jy - jp;
  leapJ = leapJ + div(n, 33) * 8 + div(mod(n, 33) + 3, 4);
  if (mod(jump, 33) === 4 && jump - n === 4) leapJ += 1;
  const leapG = div(gy, 4) - div((div(gy, 100) + 1) * 3, 4) - 150;
  const march = 20 + leapJ - leapG;
  if (jump - n < 6) n = n - jump + div(jump + 4, 33) * 33;
  let leap = mod(mod(n + 1, 33) - 1, 4);
  if (leap === -1) leap = 4;
  return { leap, gy, march };
}

function g2d(gy: number, gm: number, gd: number) {
  let d = div((gy + div(gm - 8, 6) + 100100) * 1461, 4) + div(153 * mod(gm + 9, 12) + 2, 5) + gd - 34840408;
  d = d - div(div(gy + 100100 + div(gm - 8, 6), 100) * 3, 4) + 752;
  return d;
}

function d2g(jdn: number) {
  let j = 4 * jdn + 139361631;
  j = j + div(div(4 * jdn + 183187720, 146097) * 3, 4) * 4 - 3908;
  const i = div(mod(j, 1461), 4) * 5 + 308;
  const gd = div(mod(i, 153), 5) + 1;
  const gm = mod(div(i, 153), 12) + 1;
  const gy = div(j, 1461) - 100100 + div(8 - gm, 6);
  return { gy, gm, gd };
}

function j2d(jy: number, jm: number, jd: number) {
  const r = jalCal(jy);
  return g2d(r.gy, 3, r.march) + (jm - 1) * 31 - div(jm, 7) * (jm - 7) + jd - 1;
}

export function toGregorian(jy: number, jm: number, jd: number) {
  return d2g(j2d(jy, jm, jd));
}

export function toJalali(gy: number, gm: number, gd: number) {
  const jdn = g2d(gy, gm, gd);
  let jy = gy - 621;
  const r = jalCal(jy);
  const jdn1f = g2d(gy, 3, r.march);
  let k = jdn - jdn1f;
  if (k >= 0) {
    if (k <= 185) return { jy, jm: 1 + div(k, 31), jd: mod(k, 31) + 1 };
    k -= 186;
  } else {
    jy -= 1;
    k += 179;
    if (r.leap === 1) k += 1;
  }
  return { jy, jm: 7 + div(k, 30), jd: mod(k, 30) + 1 };
}

const pad = (n: number) => String(n).padStart(2, "0");

/** "1405/08/03" (any digits, / - . separators) → ISO date "2026-10-25", or null. */
export function parseJalali(text: string): string | null {
  const latin = text.replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0)).replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660));
  const m = /^\s*(1[34]\d\d)[/\-.](\d{1,2})[/\-.](\d{1,2})\s*$/.exec(latin);
  if (!m) return null;
  const [jy, jm, jd] = [Number(m[1]), Number(m[2]), Number(m[3])];
  if (jm < 1 || jm > 12 || jd < 1 || jd > (jm <= 6 ? 31 : 30)) return null;
  const g = toGregorian(jy, jm, jd);
  const back = toJalali(g.gy, g.gm, g.gd);
  if (back.jy !== jy || back.jm !== jm || back.jd !== jd) return null; // e.g. 30 Esfand in a non-leap year
  return `${g.gy}-${pad(g.gm)}-${pad(g.gd)}`;
}

/** ISO date/time → "1405/08/03". */
export function formatJalali(iso: string | null | undefined): string {
  if (!iso) return "";
  const d = new Date(iso);
  const j = toJalali(d.getFullYear(), d.getMonth() + 1, d.getDate());
  return `${j.jy}/${pad(j.jm)}/${pad(j.jd)}`;
}
