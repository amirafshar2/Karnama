import { readFile } from "node:fs/promises";
import { join } from "node:path";
import { ImageResponse } from "next/og";
import { DEFAULT_COLORS, isHex, textOn, type Brand } from "@/lib/brand";
import { shapeWord } from "@/lib/og-shape";

export const OG_SIZE = { width: 1200, height: 630 };

/** The automatic share image (Telegram/WhatsApp/Google): the title on the brand's dark colour, the brand name below. */
export async function ogCard(title: string, brand: Brand | null, kicker?: string) {
  const font = await readFile(join(process.cwd(), "src/app/fonts/Vazirmatn-Black.ttf"));
  const colors = brand && isHex(brand.colors.primary) && isHex(brand.colors.dark) ? brand.colors : DEFAULT_COLORS;
  const ink = textOn(colors.dark);
  const words = (t: string) => t.split(/\s+/).filter(Boolean).map((w, i) => <span key={i}>{shapeWord(w)}</span>);
  const size = title.length > 70 ? 56 : title.length > 40 ? 66 : 78;
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", flexDirection: "column", justifyContent: "space-between", background: colors.dark, padding: "72px 84px", fontFamily: "Vazirmatn" }}>
        <div style={{ display: "flex", flexDirection: "row-reverse", columnGap: 14, fontSize: 30, color: colors.primary }}>{kicker ? words(kicker) : null}</div>
        <div style={{ display: "flex", flexDirection: "row-reverse", flexWrap: "wrap", columnGap: size * 0.28, fontSize: size, lineHeight: 1.45, color: ink }}>{words(title)}</div>
        <div style={{ display: "flex", flexDirection: "row-reverse", alignItems: "center", columnGap: 18 }}>
          <div style={{ width: 56, height: 12, background: colors.primary, borderRadius: 6 }} />
          <div style={{ display: "flex", flexDirection: "row-reverse", columnGap: 12, fontSize: 34, color: colors.primary }}>{words(brand?.name ?? "کارنما")}</div>
        </div>
      </div>
    ),
    { ...OG_SIZE, fonts: [{ name: "Vazirmatn", data: font, weight: 900, style: "normal" }] },
  );
}
