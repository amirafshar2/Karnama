import Link from "next/link";
import type { Brand } from "@/lib/brand";
import type { T } from "@/lib/site";
import { TilePattern } from "@/components/app/tile-pattern";

/** Logo + name from Settings › Brand (server-rendered). */
export function SiteMark({ brand }: { brand: Brand | null }) {
  const name = brand?.name ?? "کارنما";
  return (
    <span className="inline-flex items-center gap-2 text-lg font-extrabold">
      {brand?.logoUrl ? (
        // eslint-disable-next-line @next/next/no-img-element -- brand logo is uploaded at runtime
        <img src={brand.logoUrl} alt="" width={32} height={32} className="size-8 rounded-md object-contain" />
      ) : (
        <span aria-hidden="true" className="grid size-8 place-items-center rounded-md bg-[var(--brand-black)] font-black text-[var(--brand-yellow)]">
          {name.trim().charAt(0)}
        </span>
      )}
      {name}
    </span>
  );
}

export function SiteHeader({ brand, t }: { brand: Brand | null; t: T }) {
  return (
    <header className="border-b border-border bg-background/95">
      <div className="mx-auto flex h-16 max-w-6xl items-center gap-6 px-4">
        <Link href="/" className="rounded-md focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none">
          <SiteMark brand={brand} />
        </Link>
        <nav aria-label={t("site.menu")} className="flex items-center gap-1 text-sm font-semibold">
          <Link href="/plans" className="rounded-md px-3 py-2 hover:bg-muted">
            {t("site.nav.packages")}
          </Link>
          <Link href="/cases" className="hidden rounded-md px-3 py-2 hover:bg-muted sm:block">
            {t("site.nav.cases")}
          </Link>
          <Link href="/blog" className="rounded-md px-3 py-2 hover:bg-muted">
            {t("site.nav.blog")}
          </Link>
        </nav>
        <Link href="/login" className="ms-auto rounded-md border-2 border-foreground px-4 py-2 text-sm font-bold hover:bg-foreground hover:text-background">
          {t("site.nav.login")}
        </Link>
      </div>
    </header>
  );
}

export function SiteFooter({ brand, t }: { brand: Brand | null; t: T }) {
  const name = brand?.name ?? "کارنما";
  return (
    <footer className="relative mt-24 overflow-hidden bg-[var(--brand-black)] text-[#f4f4ef]">
      <TilePattern className="absolute inset-0 opacity-30 [--tile-line:rgb(254_255_0/0.12)]" id="footer-tile" />
      <div className="relative mx-auto grid max-w-6xl gap-6 px-4 py-12 sm:grid-cols-2">
        <div>
          <p className="text-xl font-extrabold text-[var(--brand-yellow)]">{name}</p>
          {brand?.tagline && <p className="mt-2 max-w-sm text-sm leading-7 opacity-80">{brand.tagline}</p>}
        </div>
        <nav aria-label={t("site.footerMenu")} className="flex flex-wrap gap-x-6 gap-y-2 text-sm sm:justify-end">
          <Link href="/" className="hover:underline">{t("site.nav.home")}</Link>
          <Link href="/plans" className="hover:underline">{t("site.nav.packages")}</Link>
          <Link href="/cases" className="hover:underline">{t("site.nav.cases")}</Link>
          <Link href="/blog" className="hover:underline">{t("site.nav.blog")}</Link>
          <Link href="/login" className="hover:underline">{t("site.nav.login")}</Link>
        </nav>
      </div>
      <p className="relative border-t border-white/10 py-4 text-center text-xs opacity-60">© {name}</p>
    </footer>
  );
}
