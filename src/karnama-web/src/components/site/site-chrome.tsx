import Link from "next/link";
import type { Brand } from "@/lib/brand";
import type { T } from "@/lib/site";
import { LogoMark } from "@/components/app/logo-mark";
import { TilePattern } from "@/components/app/tile-pattern";
import { SiteNav } from "@/components/site/motion/site-nav";

/** Logo + name from Settings › Brand (server-rendered). */
export function SiteMark({ brand, size = 32 }: { brand: Brand | null; size?: number }) {
  const name = brand?.name ?? "کارنما";
  return (
    <span className="inline-flex items-center gap-2 text-lg font-extrabold">
      {brand?.logoUrl ? (
        // eslint-disable-next-line @next/next/no-img-element -- brand logo is uploaded at runtime
        <img src={brand.logoUrl} alt="" width={size} height={size} className="rounded-lg object-contain" style={{ width: size, height: size }} />
      ) : (
        <LogoMark size={size} className="shrink-0 rounded-lg ring-1 ring-white/15" />
      )}
      {name}
    </span>
  );
}

export function SiteHeader({ brand, t }: { brand: Brand | null; t: T }) {
  return (
    <SiteNav
      brand={<SiteMark brand={brand} />}
      items={[
        { href: "/plans", label: t("site.nav.packages") },
        { href: "/cases", label: t("site.nav.cases") },
        { href: "/blog", label: t("site.nav.blog") },
      ]}
      cta={{ href: "/#consult", label: t("site.nav.consult") }}
      login={{ href: "/login", label: t("site.nav.login") }}
      menu={t("site.menu")}
      close={t("site.menuClose")}
    />
  );
}

/** Giant wordmark footer with a few tilted stickers (21st: @kedhareswer/sticker-slap-footer). */
export function SiteFooter({ brand, t }: { brand: Brand | null; t: T }) {
  const name = brand?.name ?? "کارنما";
  return (
    <footer className="foot">
      <TilePattern className="absolute inset-0 opacity-40 [--tile-line:color-mix(in_srgb,var(--brand-gold)_8%,transparent)]" id="footer-tile" />
      <div className="wrap relative grid gap-10 md:grid-cols-[1.4fr_1fr]">
        <div>
          <p className="h2 max-w-xl">{t("site.footer.cta")}</p>
          {brand?.tagline && <p className="lede">{brand.tagline}</p>}
          <Link href="/#consult" className="orb orb--hi mt-8">
            <span className="orb__dot">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" aria-hidden="true"><path d="M15 6l-6 6 6 6" /></svg>
            </span>
            {t("site.nav.consult")}
          </Link>
        </div>
        <nav aria-label={t("site.footerMenu")} className="grid content-start gap-3 text-lg font-bold md:justify-end">
          <Link href="/" className="hover:text-[var(--hi-text)]">{t("site.nav.home")}</Link>
          <Link href="/plans" className="hover:text-[var(--hi-text)]">{t("site.nav.packages")}</Link>
          <Link href="/cases" className="hover:text-[var(--hi-text)]">{t("site.nav.cases")}</Link>
          <Link href="/blog" className="hover:text-[var(--hi-text)]">{t("site.nav.blog")}</Link>
          <Link href="/login" className="hover:text-[var(--hi-text)]">{t("site.nav.login")}</Link>
        </nav>
      </div>
      <div className="relative mt-16" aria-hidden="true">
        <span className="sticker hidden bg-[var(--hi)] text-[var(--hi-fg)] sm:grid" style={{ ["--r" as string]: "-12deg", insetInlineStart: "8%", top: "-10px" }}>{t("site.footer.sticker1")}</span>
        <span className="sticker hidden bg-[#f4f2ec] text-[var(--ink)] sm:grid" style={{ ["--r" as string]: "9deg", insetInlineEnd: "12%", top: "6%" }}>{t("site.footer.sticker2")}</span>
        <span className="sticker hidden bg-[var(--tile)] text-white md:grid" style={{ ["--r" as string]: "-5deg", insetInlineStart: "46%", top: "34%" }}>{t("site.footer.sticker3")}</span>
        <p className="foot__word">
          {!brand?.logoUrl && <LogoMark size={160} className="me-4 inline-block size-[0.62em] rounded-[0.14em] align-[-0.02em]" />}
          {name}
        </p>
      </div>
      <p className="relative border-t border-white/10 py-5 text-center text-xs opacity-60">© {name}</p>
    </footer>
  );
}
