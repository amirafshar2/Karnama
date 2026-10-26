import type { Metadata } from "next";
import { PageHero } from "@/components/site/blocks";
import { CaseCard } from "@/components/site/case-card";
import { getBrand, getT, siteGet, SITE_URL, type CaseCard as C } from "@/lib/site";

export const revalidate = 60;

export async function generateMetadata(): Promise<Metadata> {
  const [t, brand] = await Promise.all([getT(), getBrand()]);
  return {
    title: { absolute: `${t("site.cases.title")} | ${brand?.name ?? "کارنما"}` },
    description: t("site.cases.description"),
    alternates: { canonical: `${SITE_URL}/cases` },
    robots: { index: true, follow: true },
  };
}

export default async function Cases() {
  const [list, t] = await Promise.all([siteGet<C[]>("/api/v1/public/site/cases"), getT()]);
  return (
    <>
      <PageHero id="cases" title={t("site.cases.title")} lede={t("site.cases.description")} />
      <section className="sec">
        <div className="wrap">
          {(list ?? []).length === 0 ? (
            <p className="rounded-3xl border border-dashed p-10 text-center opacity-70">{t("site.cases.empty")}</p>
          ) : (
            <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3" data-testid="cases-list">
              {list!.map((c, i) => <CaseCard key={c.slug} c={c} t={t} delay={(i % 3) * 100} />)}
            </div>
          )}
        </div>
      </section>
    </>
  );
}
