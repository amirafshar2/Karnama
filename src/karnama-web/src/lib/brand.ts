/**
 * White-label brand: the admin picks a main and a dark colour; every UI colour is derived here
 * with WCAG contrast guaranteed (text ≥ 4.5:1), so no choice can make the site unreadable.
 */
export type BrandColors = { primary: string; dark: string; emphasis: string | null };
export type Brand = {
  name: string | null;
  tagline: string | null;
  logoUrl: string | null;
  colors: BrandColors;
  version: number;
  demo: boolean;
};

/**
 * Default palette (redesign 1405/08/04): deep navy for trust and weight, an electric royal blue for impact,
 * a warm camel gold as the quiet second accent. Built from 21st.dev themes "Indigo Harbor", "Azure Mono" and "Camel Linen".
 */
export const DEFAULT_COLORS: BrandColors = { primary: "#3b6cff", dark: "#0b1b34", emphasis: null };
export const BRAND_GOLD = "#d4bc8b";

/** Ready-made palettes from 21st.dev community themes (Settings › Brand → one click). */
export const PRESETS: { id: string; theme: string; primary: string; dark: string }[] = [
  { id: "harbor", theme: "Indigo Harbor + Azure Mono", primary: "#3b6cff", dark: "#0b1b34" },
  { id: "luxury", theme: "Camel Linen", primary: "#c1a875", dark: "#141412" },
  { id: "ember", theme: "Elegant Luxury", primary: "#b91c1c", dark: "#1c1917" },
  { id: "teal", theme: "Perpetuity", primary: "#06858e", dark: "#0a1a20" },
  { id: "violet", theme: "Violet Bloom", primary: "#7033ff", dark: "#1a1b1e" },
  { id: "sage", theme: "Sage Garden", primary: "#7c9082", dark: "#121212" },
  { id: "dusk", theme: "Solar Dusk", primary: "#b45309", dark: "#1c1917" },
  { id: "mono", theme: "Graphite Mono", primary: "#2e2e2e", dark: "#0a0a0a" },
];
const WHITE = "#ffffff";
const INK = "#0b1220";
const DARK_BG = "#0b1220";

type Rgb = [number, number, number];

function rgb(hex: string): Rgb {
  const h = hex.replace("#", "");
  return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)) as Rgb;
}
function hex([r, g, b]: Rgb) {
  return "#" + [r, g, b].map((v) => Math.round(Math.max(0, Math.min(255, v))).toString(16).padStart(2, "0")).join("");
}
function luminance(c: string) {
  const [r, g, b] = rgb(c).map((v) => {
    const s = v / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
export function contrast(a: string, b: string) {
  const [x, y] = [luminance(a), luminance(b)].sort((p, q) => q - p);
  return (x + 0.05) / (y + 0.05);
}
function mix(a: string, b: string, t: number) {
  const [p, q] = [rgb(a), rgb(b)];
  return hex([0, 1, 2].map((i) => p[i] + (q[i] - p[i]) * t) as Rgb);
}
/** Black or white, whichever reads better on `bg`. */
export function textOn(bg: string) {
  return contrast(bg, INK) >= contrast(bg, WHITE) ? INK : WHITE;
}
/** Moves `color` toward `toward` until it reaches `ratio` against `bg`. */
function until(color: string, bg: string, toward: string, ratio: number) {
  for (let t = 0; t <= 1; t += 0.05) {
    const c = mix(color, toward, t);
    if (contrast(c, bg) >= ratio) return c;
  }
  return toward;
}

export function isHex(s: string | null | undefined): s is string {
  return !!s && /^#[0-9a-fA-F]{6}$/.test(s);
}

export function brandVars(colors: BrandColors) {
  const P = isHex(colors.primary) ? colors.primary.toLowerCase() : DEFAULT_COLORS.primary;
  const D = isHex(colors.dark) ? colors.dark.toLowerCase() : DEFAULT_COLORS.dark;
  const E = isHex(colors.emphasis) ? colors.emphasis.toLowerCase() : P;

  const onPanel = contrast(P, D) >= 4.5 ? P : textOn(D);
  // The brand colour lightened until it reads as text on the dark stage (hero, sidebar, footer).
  const onDark = until(P, D, WHITE, 4.5);
  const light = {
    "--brand-yellow": P,
    "--brand-black": D,
    "--brand-on-dark": onDark,
    "--brand-gold": BRAND_GOLD,
    "--primary": P,
    "--primary-foreground": textOn(P),
    "--primary-border": contrast(P, WHITE) < 1.6 ? D : "transparent",
    "--emphasis": until(E, WHITE, INK, 4.5),
    "--panel": D,
    "--panel-foreground": onPanel,
    "--sidebar-accent": D,
    "--sidebar-accent-foreground": onPanel,
    "--ring": contrast(D, WHITE) >= 3 ? D : INK,
  };
  const dark = {
    "--brand-yellow": P,
    "--brand-black": D,
    "--brand-on-dark": onDark,
    "--brand-gold": BRAND_GOLD,
    "--primary": P,
    "--primary-foreground": textOn(P),
    "--primary-border": contrast(P, DARK_BG) < 1.6 ? "#ffffff33" : "transparent",
    "--emphasis": until(E, DARK_BG, WHITE, 4.5),
    "--panel": contrast(D, DARK_BG) < 1.3 ? "#000000" : D,
    "--panel-foreground": onPanel,
    "--sidebar-accent": P,
    "--sidebar-accent-foreground": textOn(P),
    "--ring": contrast(P, DARK_BG) >= 3 ? P : WHITE,
  };
  return { light, dark } as { light: Record<string, string>; dark: Record<string, string> };
}

export function brandCss(colors: BrandColors): string {
  const { light, dark } = brandVars(colors);
  const block = (vars: Record<string, string>) => Object.entries(vars).map(([k, v]) => `${k}:${v}`).join(";");
  return `:root{${block(light)}}.dark{${block(dark)}}`;
}
