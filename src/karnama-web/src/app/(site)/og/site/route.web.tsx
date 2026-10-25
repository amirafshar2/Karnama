import { ogCard } from "@/components/site/og-card";
import { getBrand, getHome } from "@/lib/site";

export const revalidate = 3600;

/** Share image of the home page: the hero promise on the brand template. */
export async function GET() {
  const [home, brand] = await Promise.all([getHome(), getBrand()]);
  const res = await ogCard(home?.content.heroTitle ?? brand?.name ?? "کارنما", brand, home?.content.contact.city ?? undefined);
  res.headers.set("Cache-Control", "public, max-age=3600, s-maxage=3600");
  return res;
}
