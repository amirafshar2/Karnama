import Link from "next/link";
import type { CaseCard as C, T } from "@/lib/site";
import { faNum } from "@/lib/site";

/**
 * Portfolio card: the client's best photo full-bleed, the big number on top of it ("از ۱٬۲۰۰ به ۱۸٬۰۰۰ فالوور"),
 * and where the number came from underneath. Hover slowly zooms the photo (21st: @shadcnblockscom/gallery4 cards).
 */
export function CaseCard({ c, t, delay = 0 }: { c: C; t: T; delay?: number }) {
  return (
    <article className="group relative flex flex-col overflow-hidden rounded-[28px] bg-[var(--ink)] text-[var(--on-ink)]" data-reveal style={{ ["--d" as string]: `${delay}ms` }}>
      <div className="relative aspect-[4/5] overflow-hidden">
        {c.coverUrl ? (
          // eslint-disable-next-line @next/next/no-img-element -- uploaded photo
          <img src={c.coverUrl} alt="" loading="lazy" className="size-full object-cover transition-[scale] duration-[1200ms] ease-out group-hover:scale-[1.07]" />
        ) : (
          <div className="size-full bg-[radial-gradient(circle_at_30%_20%,var(--hi),transparent_55%),var(--ink)] opacity-60" />
        )}
        <div className="absolute inset-0 bg-gradient-to-t from-black/85 via-black/20 to-transparent" />
        {c.isSample && <span className="absolute top-4 end-4 rounded-full bg-white/90 px-3 py-1 text-xs font-extrabold text-black">{t("site.cases.sample")}</span>}
        <div className="absolute inset-x-0 bottom-0 p-6">
          {c.followerGrowth !== null && (
            <>
              <p className="text-5xl font-black text-[var(--hi-text)] tabular-nums">{t("site.cases.growth", { n: faNum(c.followerGrowth) })}</p>
              <p className="mt-1 text-sm opacity-85">
                {t("site.cases.followers")} — {t("site.cases.fromTo", { from: faNum(c.followersBefore ?? 0), to: faNum(c.followersAfter ?? 0) })}
              </p>
            </>
          )}
          <h3 className="mt-4 text-xl font-black">
            <Link href={`/cases/${c.slug}`} className="after:absolute after:inset-0">{c.businessName}</Link>
          </h3>
          <p className="text-xs opacity-70">{[c.industry, c.city].filter(Boolean).join("، ")}</p>
        </div>
      </div>
      {(c.summary || c.source || c.revenueGrowth !== null) && (
        <div className="flex flex-1 flex-col gap-2 p-6 text-sm">
          {c.summary && <p className="line-clamp-3 leading-7 opacity-85">{c.summary}</p>}
          {c.revenueGrowth !== null && <p className="font-bold text-[var(--hi-text)]">{t("site.cases.revenue")}: {t("site.cases.growth", { n: faNum(c.revenueGrowth) })}</p>}
          {c.source && <p className="mt-auto pt-2 text-xs opacity-55">{t("site.cases.source", { source: c.source })}</p>}
        </div>
      )}
    </article>
  );
}
