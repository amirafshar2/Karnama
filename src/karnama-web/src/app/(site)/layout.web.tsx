import { brandCss, DEFAULT_COLORS, isHex } from "@/lib/brand";
import { getBrand, getT } from "@/lib/site";
import { SiteFooter, SiteHeader } from "@/components/site/site-chrome";
import { SiteMotion } from "@/components/site/motion/site-motion";
import "@/components/site/site.css";

/** Public site: server-rendered HTML, brand colours inlined; only tiny motion islands, no app JavaScript (auth, live updates). */
export default async function SiteLayout({ children }: { children: React.ReactNode }) {
  const [brand, t] = await Promise.all([getBrand(), getT()]);
  const colors = brand && isHex(brand.colors.primary) && isHex(brand.colors.dark) ? brand.colors : DEFAULT_COLORS;
  return (
    <>
      <style dangerouslySetInnerHTML={{ __html: brandCss(colors) }} />
      <div className="site static flex min-h-dvh flex-col">
        <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:start-4 focus:top-4 focus:z-[70] focus:rounded-md focus:bg-background focus:p-3">
          {t("site.skip")}
        </a>
        <SiteHeader brand={brand} t={t} />
        <main id="main" className="flex-1">
          {children}
        </main>
        <SiteFooter brand={brand} t={t} />
        <SiteMotion />
      </div>
    </>
  );
}
