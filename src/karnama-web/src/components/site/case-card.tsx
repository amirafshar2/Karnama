import Link from "next/link";
import type { CaseCard as C, T } from "@/lib/site";
import { faNum } from "@/lib/site";

/** The big number first: "از ۱٬۲۰۰ به ۱۸٬۰۰۰ فالوور". Every number says where it came from. */
export function CaseCard({ c, t }: { c: C; t: T }) {
  return (
    <article className="flex flex-col overflow-hidden rounded-2xl border border-border bg-card">
      <div className="relative bg-[var(--brand-black)] p-6 text-[#f4f4ef]">
        {c.isSample && <span className="absolute top-3 end-3 rounded-full bg-white/15 px-2.5 py-0.5 text-xs font-bold">{t("site.cases.sample")}</span>}
        {c.followerGrowth !== null ? (
          <>
            <p className="text-5xl font-black text-[var(--brand-yellow)] tabular-nums">{t("site.cases.growth", { n: faNum(c.followerGrowth) })}</p>
            <p className="mt-2 text-sm opacity-80">
              {t("site.cases.followers")} — {t("site.cases.fromTo", { from: faNum(c.followersBefore ?? 0), to: faNum(c.followersAfter ?? 0) })}
            </p>
          </>
        ) : (
          <p className="text-2xl font-black text-[var(--brand-yellow)]">{c.businessName}</p>
        )}
      </div>
      <div className="flex flex-1 flex-col gap-2 p-5">
        <h3 className="text-lg font-extrabold">
          <Link href={`/cases/${c.slug}`} className="hover:underline">{c.businessName}</Link>
        </h3>
        <p className="text-xs text-muted-foreground">{[c.industry, c.city].filter(Boolean).join("، ")}</p>
        {c.summary && <p className="line-clamp-3 text-sm leading-7">{c.summary}</p>}
        {c.revenueGrowth !== null && (
          <p className="text-sm font-bold">
            {t("site.cases.revenue")}: {t("site.cases.growth", { n: faNum(c.revenueGrowth) })}
          </p>
        )}
        {c.source && <p className="mt-auto pt-2 text-xs text-muted-foreground">{t("site.cases.source", { source: c.source })}</p>}
      </div>
    </article>
  );
}
