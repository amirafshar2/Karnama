import Link from "next/link";
import type { PackageCard as P, T } from "@/lib/site";
import { toman } from "@/lib/site";
import { Check } from "@/components/site/blocks";

/**
 * Pricing tier (21st: @ibrandify/glowing-pricing-tiers): the best seller wears a spinning brand-colour border,
 * the others lift on hover. Prices come from the panel; nothing is hard-coded.
 */
export function PackageCard({ p, t, highlight, delay = 0 }: { p: P; t: T; highlight?: boolean; delay?: number }) {
  const price = p.special ?? p.price;
  const was = p.special ? p.price : p.compareAt;
  const glow = highlight || p.badge === "bestSeller";
  const inner = (
    <>
      <h3>
        <Link href={`/plans/${p.slug}`} className="hover:underline">{p.name}</Link>
      </h3>
      {p.short && <p className="tier__desc">{p.short}</p>}
      <p className="tier__price">
        {toman(price)}
        <small>{t("site.packages.toman", { n: "" }).trim()}</small>
        {was && was > price && <s className="ms-2 align-middle text-base font-semibold opacity-50">{toman(was)}</s>}
      </p>
      <p className="tier__per">{p.period === "monthly" ? t("site.packages.perMonth") : t("site.packages.perPeriod", { n: p.validityDays.toLocaleString("fa-IR") })}</p>
      <ul>
        {p.highlights.map((h) => (
          <li key={h}>
            <Check />
            {h.replace(/\d+/g, (d) => Number(d).toLocaleString("fa-IR"))}
          </li>
        ))}
      </ul>
      <Link href={`/plans/${p.slug}`} className="tier__cta">
        {t("site.packages.details")}
      </Link>
    </>
  );
  return (
    <article className={glow ? "tier tier--glow" : "tier"} data-reveal style={{ ["--d" as string]: `${delay}ms` }}>
      {p.badge !== "none" && <span className="tier__badge">{t(p.badge === "bestSeller" ? "site.badge.bestSeller" : "site.badge.new")}</span>}
      {glow ? <div className="tier__in">{inner}</div> : inner}
    </article>
  );
}
