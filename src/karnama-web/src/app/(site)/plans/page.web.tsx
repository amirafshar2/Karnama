import type { Metadata } from "next";
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
    <div className="mx-auto max-w-6xl px-4 py-12">
      <h1 className="text-4xl font-black sm:text-5xl">{t("site.packages.title")}</h1>
      <p className="mt-4 max-w-2xl text-lg leading-8 text-muted-foreground">{t("site.packages.description")}</p>
      <div className="mt-12 grid gap-8 md:grid-cols-3" data-testid="packages-list">
        {(list ?? []).map((p) => (
          <PackageCard key={p.slug} p={p} t={t} />
        ))}
      </div>
    </div>
  );
}
