/**
 * Persian text for the OG-image renderer (satori), which lays text out left-to-right and measures unshaped letters.
 * We shape here instead: each letter becomes its Arabic Presentation Form (isolated/final/initial/medial), the word is
 * reversed into visual order, and the caller puts words right-to-left. The result measures and looks right.
 */
// [isolated, final, initial, medial]; two entries = joins only to the previous letter.
const FORMS: Record<string, number[]> = {
  "ء": [0xfe80], "آ": [0xfe81, 0xfe82], "أ": [0xfe83, 0xfe84], "ؤ": [0xfe85, 0xfe86], "إ": [0xfe87, 0xfe88], "ئ": [0xfe89, 0xfe8a, 0xfe8b, 0xfe8c],
  "ا": [0xfe8d, 0xfe8e], "ب": [0xfe8f, 0xfe90, 0xfe91, 0xfe92], "ة": [0xfe93, 0xfe94], "ت": [0xfe95, 0xfe96, 0xfe97, 0xfe98], "ث": [0xfe99, 0xfe9a, 0xfe9b, 0xfe9c],
  "ج": [0xfe9d, 0xfe9e, 0xfe9f, 0xfea0], "ح": [0xfea1, 0xfea2, 0xfea3, 0xfea4], "خ": [0xfea5, 0xfea6, 0xfea7, 0xfea8], "د": [0xfea9, 0xfeaa], "ذ": [0xfeab, 0xfeac],
  "ر": [0xfead, 0xfeae], "ز": [0xfeaf, 0xfeb0], "س": [0xfeb1, 0xfeb2, 0xfeb3, 0xfeb4], "ش": [0xfeb5, 0xfeb6, 0xfeb7, 0xfeb8], "ص": [0xfeb9, 0xfeba, 0xfebb, 0xfebc],
  "ض": [0xfebd, 0xfebe, 0xfebf, 0xfec0], "ط": [0xfec1, 0xfec2, 0xfec3, 0xfec4], "ظ": [0xfec5, 0xfec6, 0xfec7, 0xfec8], "ع": [0xfec9, 0xfeca, 0xfecb, 0xfecc],
  "غ": [0xfecd, 0xfece, 0xfecf, 0xfed0], "ف": [0xfed1, 0xfed2, 0xfed3, 0xfed4], "ق": [0xfed5, 0xfed6, 0xfed7, 0xfed8], "ك": [0xfed9, 0xfeda, 0xfedb, 0xfedc],
  "ل": [0xfedd, 0xfede, 0xfedf, 0xfee0], "م": [0xfee1, 0xfee2, 0xfee3, 0xfee4], "ن": [0xfee5, 0xfee6, 0xfee7, 0xfee8], "ه": [0xfee9, 0xfeea, 0xfeeb, 0xfeec],
  "و": [0xfeed, 0xfeee], "ى": [0xfeef, 0xfef0], "ي": [0xfef1, 0xfef2, 0xfef3, 0xfef4], "پ": [0xfb56, 0xfb57, 0xfb58, 0xfb59], "چ": [0xfb7a, 0xfb7b, 0xfb7c, 0xfb7d],
  "ژ": [0xfb8a, 0xfb8b], "ک": [0xfb8e, 0xfb8f, 0xfb90, 0xfb91], "گ": [0xfb92, 0xfb93, 0xfb94, 0xfb95], "ی": [0xfbfc, 0xfbfd, 0xfbfe, 0xfbff],
};
// lam + alef ligatures: [isolated, final]
const LAM_ALEF: Record<string, number[]> = { "ا": [0xfefb, 0xfefc], "آ": [0xfef5, 0xfef6], "أ": [0xfef7, 0xfef8], "إ": [0xfef9, 0xfefa] };
const MIRROR: Record<string, string> = { "(": ")", ")": "(", "[": "]", "]": "[", "«": "»", "»": "«", "{": "}", "}": "{" };
const HARAKAT = /[ً-ٰٟ]/g;

const dual = (c: string | undefined) => !!c && (FORMS[c]?.length ?? 0) === 4;
const joins = (c: string | undefined) => !!c && !!FORMS[c];
export const hasArabic = (s: string) => /[؀-ۿ]/.test(s);

/** One word → shaped and in visual (left-to-right) order. Words without Arabic letters are returned as they are. */
export function shapeWord(word: string): string {
  if (!hasArabic(word)) return word;
  const chars = [...word.replace(HARAKAT, "")];
  const out: string[] = [];
  for (let i = 0; i < chars.length; i++) {
    const c = chars[i];
    const prev = chars[i - 1];
    const next = chars[i + 1];
    if (c === "‌" || c === "‍") continue;
    const joinPrev = dual(prev) && joins(c);
    if (c === "ل" && next && LAM_ALEF[next]) {
      out.push(String.fromCharCode(LAM_ALEF[next][joinPrev ? 1 : 0]));
      i++;
      continue;
    }
    const f = FORMS[c];
    if (!f) {
      out.push(MIRROR[c] ?? c);
      continue;
    }
    const joinNext = f.length === 4 && joins(next);
    const form = joinPrev && joinNext ? f[3] : joinPrev ? f[1] : joinNext ? f[2] : f[0];
    out.push(String.fromCharCode(form ?? f[0]));
  }
  // Visual order: reverse, but keep runs of digits/Latin left-to-right.
  const runs: string[] = [];
  for (const ch of out) {
    const ltr = /[0-9۰-۹A-Za-z.,٫٬%]/.test(ch);
    if (ltr && runs.length > 0 && /[0-9۰-۹A-Za-z.,٫٬%]$/.test(runs[runs.length - 1])) runs[runs.length - 1] += ch;
    else runs.push(ch);
  }
  return runs.reverse().join("");
}
