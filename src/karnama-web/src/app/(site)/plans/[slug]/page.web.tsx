import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { faNum, getBrand, getT, jsonLd, siteGet, SITE_URL, toman, type PackageDetail } from "@/lib/site";

export const revalidate = 60;
type Props = { params: Promise<{ slug: string }> };
const load = (slug: string) => siteGet<PackageDetail>(`/api/v1/public/site/packages/${encodeURIComponent(decodeURIComponent(slug))}`);

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  const [p, brand] = await Promise.all([load(slug), getBrand()]);
  if (!p) return { robots: { index: false } };
  const name = brand?.name ?? "کارنما";
  const title = p.seoTitle || `${p.name} | ${name}`;
  const description = p.seoDescription || p.short || undefined;
  const url = `${SITE_URL}/plans/${p.slug}`;
  return { title: { absolute: title }, description, alternates: { canonical: url }, robots: { index: true, follow: true }, openGraph: { type: "website", url, title, description, siteName: name, locale: "fa_IR" } };
}

export default async function PackagePage({ params }: Props) {
  const { slug } = await params;
  const [p, t, brand] = await Promise.all([load(slug), getT(), getBrand()]);
  if (!p) notFound();
  const url = `${SITE_URL}/plans/${p.slug}`;
  const price = p.special ?? p.price;
  const graph = [
    {
      "@type": "Service", name: p.name, description: p.short ?? undefined, url, provider: { "@type": "Organization", name: brand?.name ?? "کارنما", url: SITE_URL },
      offers: { "@type": "Offer", url, priceCurrency: "IRR", price, availability: "https://schema.org/InStock" },
    },
    { "@type": "BreadcrumbList", itemListElement: [
      { "@type": "ListItem", position: 1, name: t("site.nav.home"), item: SITE_URL },
      { "@type": "ListItem", position: 2, name: t("site.packages.title"), item: `${SITE_URL}/plans` },
      { "@type": "ListItem", position: 3, name: p.name, item: url },
    ] },
    ...(p.faq.length > 0 ? [{ "@type": "FAQPage", mainEntity: p.faq.map((f) => ({ "@type": "Question", name: f.question, acceptedAnswer: { "@type": "Answer", text: f.answer } })) }] : []),
  ];
  const facts = [
    p.consultSessions > 0 && t("site.packages.consults", { n: faNum(p.consultSessions) }),
    p.revisionsPerItem > 0 && t("site.packages.revisions", { n: faNum(p.revisionsPerItem) }),
    p.deliveryHours > 0 && t("site.packages.delivery", { n: faNum(p.deliveryHours) }),
    t(p.management === "clientManaged" ? "site.packages.clientManaged" : "site.packages.agencyManaged"),
  ].filter(Boolean) as string[];
  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLd({ "@context": "https://schema.org", "@graph": graph }) }} />
      <article className="mx-auto grid max-w-6xl gap-12 px-4 py-12 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0">
          <nav aria-label={t("site.blog.breadcrumb")} className="text-sm text-muted-foreground">
            <Link href="/" className="hover:underline">{t("site.nav.home")}</Link> / <Link href="/plans" className="hover:underline">{t("site.packages.title")}</Link>
          </nav>
          <h1 className="mt-4 text-4xl font-black sm:text-5xl">{p.name}</h1>
          {p.short && <p className="mt-4 text-lg leading-8 text-muted-foreground">{p.short}</p>}
          {p.quotas.length > 0 && (
            <section className="mt-10">
              <h2 className="text-2xl font-extrabold">{t("site.packages.includes")}</h2>
              <ul className="mt-4 grid gap-3 sm:grid-cols-2">
                {p.quotas.map((q) => (
                  <li key={q} className="rounded-xl border-2 border-border bg-card px-4 py-3 font-bold">{q.replace(/\d+/g, (d) => faNum(Number(d)))}</li>
                ))}
              </ul>
            </section>
          )}
          {p.descriptionHtml && <div className="prose-fa mt-10" dangerouslySetInnerHTML={{ __html: p.descriptionHtml }} />}
          {p.features.length > 0 && (
            <section className="mt-10">
              <h2 className="text-2xl font-extrabold">{t("site.packages.features")}</h2>
              <ul className="mt-4 space-y-2">
                {p.features.map((f) => (
                  <li key={f} className="flex gap-2"><span aria-hidden="true" className="mt-2.5 size-2 shrink-0 rounded-full bg-foreground" />{f}</li>
                ))}
              </ul>
            </section>
          )}
          {p.faq.length > 0 && (
            <section className="mt-10">
              <h2 className="text-2xl font-extrabold">{t("site.packages.faq")}</h2>
              <div className="mt-4 divide-y rounded-2xl border bg-card">
                {p.faq.map((f) => (
                  <details key={f.question} className="p-5">
                    <summary className="cursor-pointer font-bold">{f.question}</summary>
                    <p className="mt-2 leading-8 text-muted-foreground">{f.answer}</p>
                  </details>
                ))}
              </div>
            </section>
          )}
        </div>
        <aside>
          <div className="sticky top-6 rounded-2xl border-2 border-foreground bg-card p-6 shadow-[6px_6px_0_var(--primary)]">
            <p className="text-3xl font-black tabular-nums">{t("site.packages.toman", { n: toman(price) })}</p>
            {p.special && <s className="text-sm text-muted-foreground">{toman(p.price)}</s>}
            <p className="text-sm text-muted-foreground">{p.period === "monthly" ? t("site.packages.perMonth") : t("site.packages.perPeriod", { n: faNum(p.validityDays) })}</p>
            <ul className="mt-5 space-y-2 text-sm">{facts.map((f) => <li key={f}>• {f}</li>)}</ul>
            <Link href="/#consult" className="mt-6 grid h-12 place-items-center rounded-md border-2 border-foreground bg-primary font-extrabold text-primary-foreground hover:bg-foreground hover:text-background">
              {t("site.packages.consult")}
            </Link>
          </div>
        </aside>
      </article>
    </>
  );
}
