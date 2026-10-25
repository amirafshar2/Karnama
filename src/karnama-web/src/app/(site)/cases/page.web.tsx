import type { Metadata } from "next";
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
    <div className="mx-auto max-w-6xl px-4 py-12">
      <h1 className="text-4xl font-black sm:text-5xl">{t("site.cases.title")}</h1>
      <p className="mt-4 max-w-2xl text-lg leading-8 text-muted-foreground">{t("site.cases.description")}</p>
      {(list ?? []).length === 0 ? (
        <p className="mt-12 rounded-2xl border border-dashed p-10 text-center text-muted-foreground">{t("site.cases.empty")}</p>
      ) : (
        <div className="mt-12 grid gap-6 md:grid-cols-3" data-testid="cases-list">
          {list!.map((c) => <CaseCard key={c.slug} c={c} t={t} />)}
        </div>
      )}
    </div>
  );
}
