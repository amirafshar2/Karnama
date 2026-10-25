import type { Metadata } from "next";
import Link from "next/link";
import { TilePattern } from "@/components/app/tile-pattern";
import { CaseCard } from "@/components/site/case-card";
import { ConsultForm } from "@/components/site/consult-form";
import { PackageCard } from "@/components/site/package-card";
import { PostCard } from "@/components/site/post-card";
import { ServiceIcon } from "@/components/site/service-icon";
import { faNum, getBrand, getHome, getT, jsonLd, siteGet, SITE_URL, type BlogList } from "@/lib/site";

export const revalidate = 60;

export async function generateMetadata(): Promise<Metadata> {
  const [home, brand] = await Promise.all([getHome(), getBrand()]);
  const name = brand?.name ?? "کارنما";
  const title = home?.content.seoTitle ?? name;
  const description = home?.content.seoDescription ?? brand?.tagline ?? undefined;
  return {
    title: { absolute: title.includes(name) ? title : `${title} | ${name}` },
    description,
    alternates: { canonical: `${SITE_URL}/`, languages: { fa: `${SITE_URL}/`, "x-default": `${SITE_URL}/` } },
    robots: { index: true, follow: true },
    openGraph: { type: "website", url: `${SITE_URL}/`, title, description, siteName: name, locale: "fa_IR", images: [{ url: `${SITE_URL}/og/site`, width: 1200, height: 630 }] },
    twitter: { card: "summary_large_image", title, description, images: [`${SITE_URL}/og/site`] },
  };
}

export default async function Home() {
  const [home, brand, t, posts] = await Promise.all([getHome(), getBrand(), getT(), siteGet<BlogList>("/api/v1/public/blog?page=1")]);
  const name = brand?.name ?? "کارنما";
  if (!home) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-24">
        <h1 className="text-4xl font-black">{name}</h1>
      </div>
    );
  }
  const c = home.content;
  const k = c.contact;
  const org = {
    "@type": ["Organization", "ProfessionalService"],
    "@id": `${SITE_URL}/#org`,
    name,
    url: `${SITE_URL}/`,
    ...(brand?.logoUrl ? { logo: `${SITE_URL}${brand.logoUrl}`, image: `${SITE_URL}${brand.logoUrl}` } : {}),
    description: c.seoDescription,
    ...(k.phone ? { telephone: k.phone } : {}),
    ...(k.email ? { email: k.email } : {}),
    areaServed: k.city ?? "شیراز",
    ...(k.address || k.city ? { address: { "@type": "PostalAddress", streetAddress: k.address ?? undefined, addressLocality: k.city ?? undefined, addressCountry: "IR" } } : {}),
    ...(k.latitude && k.longitude ? { geo: { "@type": "GeoCoordinates", latitude: k.latitude, longitude: k.longitude } } : {}),
    sameAs: [k.instagram, k.telegram].filter(Boolean).map((h) => (h!.startsWith("http") ? h : `https://instagram.com/${h!.replace(/^@/, "")}`)),
    hasOfferCatalog: {
      "@type": "OfferCatalog",
      name: c.packagesTitle,
      itemListElement: home.packages.map((p) => ({
        "@type": "Offer",
        url: `${SITE_URL}/plans/${p.slug}`,
        priceCurrency: "IRR",
        price: p.special ?? p.price,
        itemOffered: { "@type": "Service", name: p.name, description: p.short ?? undefined },
      })),
    },
  };
  const graph = [
    org,
    { "@type": "WebSite", "@id": `${SITE_URL}/#site`, url: `${SITE_URL}/`, name, inLanguage: "fa-IR", publisher: { "@id": `${SITE_URL}/#org` } },
    ...(c.faq.length > 0 ? [{ "@type": "FAQPage", mainEntity: c.faq.map((f) => ({ "@type": "Question", name: f.question, acceptedAnswer: { "@type": "Answer", text: f.answer } })) }] : []),
  ];
  const stats = [
    home.stats.clients > 0 && { n: faNum(home.stats.clients), label: t("site.stats.clients") },
    home.stats.deliveries > 0 && { n: faNum(home.stats.deliveries), label: t("site.stats.deliveries") },
    home.stats.medianFollowerGrowth !== null && { n: `${faNum(home.stats.medianFollowerGrowth)}٪`, label: t("site.stats.growth") },
  ].filter(Boolean) as { n: string; label: string }[];

  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLd({ "@context": "https://schema.org", "@graph": graph }) }} />

      {/* Hero: the agency's promise on its own colours, the Shiraz tile lattice behind it. */}
      <section className="relative overflow-hidden bg-[var(--brand-black)] text-[#f4f4ef]">
        <TilePattern className="absolute inset-0 [--tile-line:rgb(254_255_0/0.10)]" id="hero-tile" />
        <div className="relative mx-auto max-w-6xl px-4 py-20 sm:py-28">
          <h1 className="max-w-4xl text-4xl leading-[1.45] font-black sm:text-6xl sm:leading-[1.3]" data-testid="home-hero">
            {c.heroTitle}
          </h1>
          {c.heroSubtitle && <p className="mt-6 max-w-2xl text-lg leading-9 opacity-85">{c.heroSubtitle}</p>}
          <div className="mt-10 flex flex-wrap gap-3">
            <a href="#consult" className="inline-grid h-14 place-items-center rounded-md bg-[var(--brand-yellow)] px-8 text-lg font-extrabold text-[var(--brand-black)] hover:bg-white">
              {c.ctaLabel}
            </a>
            {home.cases.length > 0 && (
              <Link href="/cases" className="inline-grid h-14 place-items-center rounded-md border-2 border-white/40 px-6 font-bold hover:border-white">
                {c.casesTitle}
              </Link>
            )}
          </div>
          {stats.length > 0 && (
            <dl className="mt-16 grid max-w-3xl gap-6 border-t border-white/15 pt-8 sm:grid-cols-3">
              {stats.map((s) => (
                <div key={s.label}>
                  <dt className="text-sm opacity-70">{s.label}</dt>
                  <dd className="mt-1 text-4xl font-black text-[var(--brand-yellow)] tabular-nums">{s.n}</dd>
                </div>
              ))}
            </dl>
          )}
        </div>
      </section>

      {home.services.length > 0 && (
        <section aria-labelledby="services" className="mx-auto max-w-6xl px-4 py-20">
          <h2 id="services" className="text-3xl font-black">{c.servicesTitle}</h2>
          <ul className="mt-10 grid gap-x-8 gap-y-10 sm:grid-cols-2 lg:grid-cols-3">
            {home.services.map((s) => (
              <li key={s.name} className="flex gap-4">
                <span className="grid size-12 shrink-0 place-items-center rounded-xl bg-primary text-primary-foreground ring-2 ring-foreground">
                  <ServiceIcon name={s.icon} className="size-6" />
                </span>
                <div>
                  <h3 className="text-lg font-extrabold">{s.name}</h3>
                  {s.description && <p className="mt-1 text-sm leading-7 text-muted-foreground">{s.description}</p>}
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {home.cases.length > 0 && (
        <section aria-labelledby="cases" className="bg-muted/50 py-20">
          <div className="mx-auto max-w-6xl px-4">
            <div className="flex flex-wrap items-end justify-between gap-4">
              <h2 id="cases" className="text-3xl font-black">{c.casesTitle}</h2>
              <Link href="/cases" className="font-semibold underline underline-offset-4">{t("site.cases.all")}</Link>
            </div>
            <div className="mt-10 grid gap-6 md:grid-cols-3">
              {home.cases.map((x) => (
                <CaseCard key={x.slug} c={x} t={t} />
              ))}
            </div>
          </div>
        </section>
      )}

      {home.packages.length > 0 && (
        <section aria-labelledby="packages" className="mx-auto max-w-6xl px-4 py-20" id="packages-list">
          <h2 id="packages" className="text-3xl font-black">{c.packagesTitle}</h2>
          <div className="mt-12 grid gap-8 md:grid-cols-3">
            {home.packages.slice(0, 3).map((p) => (
              <PackageCard key={p.slug} p={p} t={t} />
            ))}
          </div>
          {home.packages.length > 3 && (
            <Link href="/plans" className="mt-8 inline-block font-semibold underline underline-offset-4">{t("site.nav.packages")}</Link>
          )}
        </section>
      )}

      {c.process.length > 0 && (
        <section aria-labelledby="process" className="border-y border-border py-20">
          <div className="mx-auto max-w-6xl px-4">
            <h2 id="process" className="text-3xl font-black">{c.processTitle}</h2>
            <ol className="mt-10 grid gap-8 sm:grid-cols-2 lg:grid-cols-4">
              {c.process.map((s, i) => (
                <li key={s.title} className="relative">
                  <span className="text-5xl font-black text-foreground/15 tabular-nums" aria-hidden="true">{faNum(i + 1)}</span>
                  <h3 className="mt-1 text-lg font-extrabold">{s.title}</h3>
                  <p className="mt-2 text-sm leading-7 text-muted-foreground">{s.body}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>
      )}

      {home.testimonials.length > 0 && (
        <section aria-labelledby="quotes" className="mx-auto max-w-6xl px-4 py-20">
          <h2 id="quotes" className="text-3xl font-black">{c.testimonialsTitle}</h2>
          <div className="mt-10 grid gap-6 md:grid-cols-3">
            {home.testimonials.slice(0, 3).map((q) => (
              <figure key={q.slug} className="rounded-2xl border-s-8 border-primary bg-card p-6">
                <blockquote className="text-lg leading-9 font-semibold">«{q.quote}»</blockquote>
                <figcaption className="mt-4 text-sm text-muted-foreground">
                  {q.by} — <Link href={`/cases/${q.slug}`} className="underline">{q.businessName}</Link>
                </figcaption>
              </figure>
            ))}
          </div>
        </section>
      )}

      {c.showBlog && (posts?.items.length ?? 0) > 0 && (
        <section aria-labelledby="latest" className="bg-muted/50 py-20">
          <div className="mx-auto max-w-6xl px-4">
            <div className="flex items-end justify-between">
              <h2 id="latest" className="text-3xl font-black">{c.blogTitle}</h2>
              <Link href="/blog" className="font-semibold underline underline-offset-4">{t("site.nav.blog")}</Link>
            </div>
            <div className="mt-10 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
              {posts!.items.slice(0, 3).map((p) => (
                <PostCard key={p.slug} post={p} t={t} />
              ))}
            </div>
          </div>
        </section>
      )}

      {c.faq.length > 0 && (
        <section aria-labelledby="faq" className="mx-auto max-w-3xl px-4 py-20">
          <h2 id="faq" className="text-3xl font-black">{c.faqTitle}</h2>
          <div className="mt-8 divide-y rounded-2xl border bg-card">
            {c.faq.map((f) => (
              <details key={f.question} className="group p-5">
                <summary className="cursor-pointer list-none text-lg font-bold marker:hidden">
                  <span className="me-2 inline-block transition-transform group-open:rotate-45" aria-hidden="true">+</span>
                  {f.question}
                </summary>
                <p className="mt-3 leading-8 text-muted-foreground">{f.answer}</p>
              </details>
            ))}
          </div>
        </section>
      )}

      <section id="consult" aria-labelledby="consult-title" className="scroll-mt-4 border-t-4 border-foreground bg-primary/15 py-20">
        <div className="mx-auto grid max-w-6xl gap-12 px-4 lg:grid-cols-[1.2fr_1fr]">
          <div>
            <h2 id="consult-title" className="text-3xl font-black">{c.consultTitle}</h2>
            <p className="mt-3 max-w-xl leading-8 text-muted-foreground">{c.consultText}</p>
            <div className="mt-8">
              <ConsultForm
                source="/"
                texts={{
                  name: t("site.form.name"), phone: t("site.form.phone"), business: t("site.form.business"), message: t("site.form.message"),
                  send: t("site.form.send"), sending: t("site.form.sending"), sent: t("site.form.sent"), error: t("site.form.error"),
                }}
              />
            </div>
          </div>
          <address className="space-y-3 not-italic">
            <h3 className="text-xl font-extrabold">{t("site.contact.title")}</h3>
            {k.phone && <p><a href={`tel:${k.phone}`} dir="ltr" className="text-2xl font-black hover:underline">{k.phone}</a></p>}
            {(k.address || k.city) && <p className="leading-8">{[k.city, k.address].filter(Boolean).join("، ")}</p>}
            {k.hours && <p className="text-sm text-muted-foreground">{t("site.contact.hours")}: {k.hours}</p>}
            <div className="flex flex-wrap gap-2 pt-2">
              {k.neshanUrl && <a href={k.neshanUrl} target="_blank" rel="noopener noreferrer" className="rounded-md border-2 border-foreground px-3 py-2 text-sm font-bold">{t("site.contact.neshan")}</a>}
              {k.baladUrl && <a href={k.baladUrl} target="_blank" rel="noopener noreferrer" className="rounded-md border-2 border-foreground px-3 py-2 text-sm font-bold">{t("site.contact.balad")}</a>}
            </div>
          </address>
        </div>
      </section>
    </>
  );
}
