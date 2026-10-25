import { clsx, type ClassValue } from "clsx";

/**
 * Joins classes and lets later Tailwind utilities override earlier ones of the same kind
 * (e.g. cn("h-9 px-4", "h-12") → "px-4 h-12"). A ~1 KB replacement for tailwind-merge (~9 KB gzip),
 * covering the utility groups this app's components actually override. Parity with tailwind-merge
 * for those cases is checked by scripts/cn-parity.mts.
 */
export function cn(...inputs: ClassValue[]) {
  const tokens = clsx(inputs).split(" ").filter(Boolean);
  const seen = new Set<string>();
  const out: string[] = [];
  // Right to left: a class survives unless a later class overrides its group.
  for (let i = tokens.length - 1; i >= 0; i--) {
    const token = tokens[i];
    const keys = groupKeys(token);
    if (keys === null) {
      out.push(token);
      continue;
    }
    // keys[0] is the class's own group; all keys are the groups it overrides.
    if (seen.has(keys[0])) continue;
    keys.forEach((k) => seen.add(k));
    out.push(token);
  }
  return out.reverse().join(" ");
}

const TEXT_SIZE = /^text-(xs|sm|base|lg|[2-9]?xl|\[[\d.]+(px|rem|em)\])(\/.*)?$/;
const TEXT_ALIGN = /^text-(left|right|center|justify|start|end)$/;
const DISPLAY = /^(block|inline|inline-block|flex|inline-flex|grid|inline-grid|hidden|contents|table|flow-root)$/;
const POSITION = /^(static|fixed|absolute|relative|sticky)$/;
const FONT_WEIGHT = /^font-(thin|extralight|light|normal|medium|semibold|bold|extrabold|black)$/;
const BORDER_WIDTH = /^border(-\d+|-\[[^\]]+px\])?$/;
const BORDER_SIDE = /^border-([xytbserl])(-\d+)?$/;
const BORDER_STYLE = /^border-(solid|dashed|dotted|double|none|hidden)$/;
const PAD = /^p([xytbserl]?)-/;
const MARGIN = /^-?m([xytbserl]?)-/;
const ROUNDED_SIDE = /^rounded-(t|b|s|e|l|r|ss|se|es|ee|tl|tr|bl|br)(-|$)/;

const SIMPLE: [RegExp, string][] = [
  [/^(min-w)-/, "min-w"],
  [/^(max-w)-/, "max-w"],
  [/^(min-h)-/, "min-h"],
  [/^(max-h)-/, "max-h"],
  [/^w-/, "w"],
  [/^h-/, "h"],
  [/^gap-x-/, "gap-x"],
  [/^gap-y-/, "gap-y"],
  [/^justify-(start|end|center|between|around|evenly|stretch|normal)$/, "justify"],
  [/^items-/, "items"],
  [/^self-/, "self"],
  [/^whitespace-/, "whitespace"],
  [/^overflow-x-/, "overflow-x"],
  [/^overflow-y-/, "overflow-y"],
  [/^overflow-/, "overflow"],
  [/^flex-(row|col)(-reverse)?$/, "flex-dir"],
  [/^flex-(wrap|nowrap|wrap-reverse)$/, "flex-wrap"],
  [/^font-(sans|serif|mono)$/, "font-family"],
  [/^shadow(-|$)/, "shadow"],
  [/^opacity-/, "opacity"],
  [/^cursor-/, "cursor"],
  [/^z-/, "z"],
  [/^leading-/, "leading"],
  [/^tracking-/, "tracking"],
  [/^bg-/, "bg"],
  [/^top-/, "top"],
  [/^bottom-/, "bottom"],
  [/^start-/, "start"],
  [/^end-/, "end"],
  [/^left-/, "left"],
  [/^right-/, "right"],
  [/^inset-/, "inset"],
];

/** Conflict keys for one class (variant-aware), or null when it never conflicts. */
function groupKeys(token: string): string[] | null {
  // Split off variants like "md:", "dark:", "data-[state=open]:" (colons inside [] belong to the value).
  let depth = 0;
  let cut = -1;
  for (let i = 0; i < token.length; i++) {
    const c = token[i];
    if (c === "[") depth++;
    else if (c === "]") depth--;
    else if (c === ":" && depth === 0) cut = i;
  }
  const variant = cut >= 0 ? token.slice(0, cut + 1) : "";
  const base = (cut >= 0 ? token.slice(cut + 1) : token).replace(/^!/, "");
  const key = (k: string) => variant + k;

  if (base.startsWith("size-")) return [key("size"), key("w"), key("h")];
  const pad = PAD.exec(base);
  if (pad) return paddingKeys("p", pad[1]).map(key);
  const margin = MARGIN.exec(base);
  if (margin) return paddingKeys("m", margin[1]).map(key);
  if (base.startsWith("gap-") && !base.startsWith("gap-x-") && !base.startsWith("gap-y-")) return [key("gap"), key("gap-x"), key("gap-y")];
  if (ROUNDED_SIDE.test(base)) return [key("rounded-" + ROUNDED_SIDE.exec(base)![1])];
  if (base === "rounded" || base.startsWith("rounded-"))
    return [key("rounded"), ...["t", "b", "s", "e", "l", "r", "ss", "se", "es", "ee", "tl", "tr", "bl", "br"].map((s) => key("rounded-" + s))];
  if (TEXT_SIZE.test(base)) return [key("text-size")];
  if (TEXT_ALIGN.test(base)) return [key("text-align")];
  if (base.startsWith("text-")) return [key("text-color")];
  if (DISPLAY.test(base)) return [key("display")];
  if (POSITION.test(base)) return [key("position")];
  if (FONT_WEIGHT.test(base)) return [key("font-weight")];
  if (BORDER_WIDTH.test(base)) return [key("border-w")];
  if (BORDER_SIDE.test(base)) return [key("border-w-" + BORDER_SIDE.exec(base)![1])];
  if (BORDER_STYLE.test(base)) return [key("border-style")];
  if (base.startsWith("border-") && !base.startsWith("border-spacing")) return [key("border-color")];
  for (const [re, k] of SIMPLE) if (re.test(base)) return [key(k)];
  return null;
}

/** Own group first. p-4 overrides every side; px-4 overrides x, s, e, l, r; ps-4 only s. */
function paddingKeys(prefix: string, side: string): string[] {
  const all = ["", "x", "y", "t", "b", "s", "e", "l", "r"].map((s) => prefix + s);
  if (side === "") return all;
  if (side === "x") return [prefix + "x", prefix + "s", prefix + "e", prefix + "l", prefix + "r"];
  if (side === "y") return [prefix + "y", prefix + "t", prefix + "b"];
  return [prefix + side];
}
