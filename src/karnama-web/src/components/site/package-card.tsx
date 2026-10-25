import Link from "next/link";
import type { PackageCard as P, T } from "@/lib/site";
import { toman } from "@/lib/site";
import { cn } from "@/lib/utils";

export function PackageCard({ p, t, highlight }: { p: P; t: T; highlight?: boolean }) {
  const price = p.special ?? p.price;
  const was = p.special ? p.price : p.compareAt;
  return (
    <article className={cn("relative flex flex-col rounded-2xl border-2 bg-card p-6", highlight || p.badge === "bestSeller" ? "border-foreground shadow-[6px_6px_0_var(--primary)]" : "border-border")}>
      {p.badge !== "none" && (
        <span className="absolute -top-3 start-6 rounded-full bg-primary px-3 py-0.5 text-xs font-extrabold text-primary-foreground ring-2 ring-foreground">
          {t(p.badge === "bestSeller" ? "site.badge.bestSeller" : "site.badge.new")}
        </span>
      )}
      <h3 className="text-xl font-extrabold">
        <Link href={`/plans/${p.slug}`} className="hover:underline">{p.name}</Link>
      </h3>
      {p.short && <p className="mt-2 text-sm leading-7 text-muted-foreground">{p.short}</p>}
      <p className="mt-5">
        <span className="text-3xl font-black tabular-nums">{t("site.packages.toman", { n: toman(price) })}</span>
        {was && was > price && <s className="ms-2 text-sm text-muted-foreground">{toman(was)}</s>}
        <span className="block text-xs text-muted-foreground">{p.period === "monthly" ? t("site.packages.perMonth") : t("site.packages.perPeriod", { n: p.validityDays.toLocaleString("fa-IR") })}</span>
      </p>
      <ul className="mt-5 flex-1 space-y-2 text-sm">
        {p.highlights.map((h) => (
          <li key={h} className="flex items-start gap-2">
            <span aria-hidden="true" className="mt-2 size-2 shrink-0 rounded-full bg-foreground" />
            {h.replace(/\d+/g, (d) => Number(d).toLocaleString("fa-IR"))}
          </li>
        ))}
      </ul>
      <Link href={`/plans/${p.slug}`} className="mt-6 grid h-12 place-items-center rounded-md border-2 border-foreground font-bold hover:bg-foreground hover:text-background">
        {t("site.packages.details")}
      </Link>
    </article>
  );
}
