import type { Metadata } from "next";
import { PageHero } from "@/components/site/blocks";
import { PackageCard } from "@/components/site/package-card";
import { getBrand, getT, siteGet, SITE_URL, type PackageCard as P } from "@/lib/site";

export const revalidate = 60;

export async function generateMetadata(): Promise<Metadata> {
  const [t, brand] = await Promise.all([getT(), getBrand()]);
  return {
    title: { absolute: `${t("site.packages.title")} | ${brand?.name ?? "کارنما"}` },
    description: t("site.packages.description"),
    alternates: { canonical: `${SITE_URL}/plans` },
    robots: { index: true, follow: true },
  };
}

export default async function Packages() {
  const [list, t] = await Promise.all([siteGet<P[]>("/api/v1/public/site/packages"), getT()]);
  return (
    <>
      <PageHero id="plans" title={t("site.packages.title")} lede={t("site.packages.description")} />
      <section className="sec">
        <div className="wrap tiers" data-testid="packages-list">
          {(list ?? []).map((p, i) => (
            <PackageCard key={p.slug} p={p} t={t} delay={(i % 3) * 100} />
          ))}
        </div>
      </section>
    </>
  );
}
