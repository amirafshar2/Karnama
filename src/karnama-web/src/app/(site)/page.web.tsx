import type { Metadata } from "next";
import Link from "next/link";
import { TilePattern } from "@/components/app/tile-pattern";
import { Arrow } from "@/components/site/blocks";
import { ConsultForm } from "@/components/site/consult-form";
import { HeroTilt } from "@/components/site/motion/hero-tilt";
import { HoverReel } from "@/components/site/motion/hover-reel";
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
      <div className="wrap pt-40 pb-24">
        <h1 className="h2">{name}</h1>
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

  const hero = home.hero ?? { videoUrl: null, posterUrl: null, images: [] };
  const fan = hero.images.slice(0, 5);
  // Centred fan: offsets −2…2 for five cards, fewer cards close up around the middle.
  const fanPos = fan.map((_, i) => {
    const x = i - (fan.length - 1) / 2;
    return { x, r: x * 8, z: 10 - Math.abs(x) * 2, d: 750 + Math.abs(x) * 150 };
  });
  const words = c.heroTitle.split(/\s+/).filter(Boolean);
  const handle = (k.instagram ?? name).replace(/^https?:\/\/(www\.)?instagram\.com\//, "").replace(/^@/, "").replace(/\/$/, "");
  const bandItems = [...home.services.map((s) => s.name), ...home.cases.map((x) => x.industry).filter(Boolean) as string[]].filter((v, i, a) => a.indexOf(v) === i);
  const quotes = home.testimonials.length > 0 ? [...home.testimonials, ...home.testimonials, ...home.testimonials].slice(0, Math.max(6, home.testimonials.length)) : [];
  const servicePhoto = hero.images[5] ?? hero.images[0] ?? null;
  const tone = ["", "bento__hi", "", "bento__ink", "", "", "bento__hi", ""];

  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLd({ "@context": "https://schema.org", "@graph": graph }) }} />

      {/* Hero: a looping montage behind, the promise in front, a fan of real posts rising from the bottom. */}
      <section className="hero" aria-labelledby="hero-title">
        <div className="hero__media" aria-hidden="true">
          {/* Still first (lazy: never holds up the page load); the loop is attached after load by HeroTilt, skipped on slow/data-saver connections. */}
          {hero.posterUrl && (
            // eslint-disable-next-line @next/next/no-img-element -- uploaded hero still
            <img src={hero.posterUrl} alt="" loading="lazy" decoding="async" />
          )}
          {hero.videoUrl && <video muted loop playsInline preload="none" data-src={hero.videoUrl} className="absolute inset-0 opacity-0 transition-opacity duration-1000" />}
        </div>
        <div className="hero__shade" aria-hidden="true" />
        <TilePattern className="hero__tile [--tile-line:color-mix(in_srgb,var(--brand-gold)_9%,transparent)]" id="hero-tile" />
        <div className="hero__copy">
          <p className="hero__pill">
            <b>{t("site.hero.badge")}</b>
            {t("site.hero.pill")}
          </p>
          <h1 id="hero-title" className="hero__title" data-testid="home-hero">
            {words.map((w, i) => (
              <span key={i}>
                <span className="w" style={{ ["--i" as string]: i }}>{w}</span>
                {i < words.length - 1 ? " " : ""}
              </span>
            ))}
          </h1>
          {c.heroSubtitle && <p className="hero__sub">{c.heroSubtitle}</p>}
          <div className="hero__ctas">
            <a href="#consult" className="orb orb--hi">
              <span className="orb__dot"><Arrow /></span>
              {c.ctaLabel}
            </a>
            {home.cases.length > 0 && (
              <Link href="/cases" className="orb orb--ghost">
                <span className="orb__dot"><Arrow /></span>
                {c.casesTitle}
              </Link>
            )}
          </div>
        </div>
        {fan.length > 0 && (
          <div className="fan" data-fan aria-hidden="true">
            {fan.map((src, i) => {
              const p = fanPos[i];
              return (
                <figure key={src} className="fan__card" style={{ ["--x" as string]: p.x, ["--r" as string]: p.r, ["--z" as string]: p.z, ["--d" as string]: `${p.d}ms` }}>
                  {/* eslint-disable-next-line @next/next/no-img-element -- uploaded post photo */}
                  <img src={src} alt="" loading="lazy" decoding="async" />
                  <figcaption><i />{handle}</figcaption>
                  <span className="fan__like">
                    <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 21s-7.5-4.6-9.6-9.2C.9 8.4 3 5 6.4 5c2 0 3.5 1.1 4.3 2.6h2.6C14.1 6.1 15.6 5 17.6 5 21 5 23.1 8.4 21.6 11.8 19.5 16.4 12 21 12 21z" /></svg>
                    {faNum([1240, 3180, 8730, 2410, 960][i] ?? 1200)}
                  </span>
                </figure>
              );
            })}
          </div>
        )}
        <span className="hero__scroll" aria-hidden="true">{t("site.hero.scroll")}</span>
        <HeroTilt />
      </section>

      {bandItems.length > 0 && (
        <div className="band" aria-hidden="true">
          <div className="marquee" style={{ ["--speed" as string]: "34s" }}>
            {[0, 1].map((k2) => (
              <div className="marquee__track" key={k2}>
                {bandItems.map((x) => (
                  <span className="band__item" key={x}>
                    {x}
                    <svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2l2.6 6.9L22 12l-7.4 3.1L12 22l-2.6-6.9L2 12l7.4-3.1z" /></svg>
                  </span>
                ))}
              </div>
            ))}
          </div>
        </div>
      )}

      {stats.length > 0 && (
        <section aria-labelledby="stats" className="sec sec--ink">
          <div className="wrap">
            <div className="sec__head">
              <div>
                <h2 id="stats" className="h2" data-reveal>{t("site.stats.title")}</h2>
                <p className="lede" data-reveal>{t("site.stats.lede")}</p>
              </div>
            </div>
            <dl className="rings">
              {stats.map((s, i) => (
                <div key={s.label} className="ostat" data-observe style={{ ["--p" as string]: 100 }}>
                  <svg className="ring__svg" viewBox="0 0 120 120" aria-hidden="true">
                    <circle className="ring__track" cx="60" cy="60" r="52" pathLength={100} />
                    <circle className="ring__arc" cx="60" cy="60" r="52" pathLength={100} style={{ transitionDelay: `${200 + i * 180}ms` }} />
                  </svg>
                  <span className="ring__orbit" aria-hidden="true" style={{ animationDelay: `${-i * 4}s` }} />
                  <dd className="ring__value">{s.n}</dd>
                  <dt className="ring__label">{s.label}</dt>
                </div>
              ))}
            </dl>
          </div>
        </section>
      )}

      {home.services.length > 0 && (
        <section aria-labelledby="services" className="sec">
          <div className="wrap">
            <div className="sec__head">
              <div>
                <h2 id="services" className="h2" data-reveal>{c.servicesTitle}</h2>
                <p className="lede" data-reveal>{t("site.services.lede")}</p>
              </div>
            </div>
            <ul className="bento">
              {servicePhoto && (
                <li className="bento__cell bento__photo" data-reveal>
                  {/* eslint-disable-next-line @next/next/no-img-element -- uploaded photo */}
                  <img src={servicePhoto} alt="" loading="lazy" />
                  <div className="bento__over"><b>{t("site.services.photo")}</b></div>
                </li>
              )}
              {home.services.map((s, i) => (
                <li key={s.name} className={`bento__cell ${tone[i % tone.length]}`} data-reveal style={{ ["--d" as string]: `${(i % 3) * 90}ms` }}>
                  <span className="bento__icon"><ServiceIcon name={s.icon} className="size-6" /></span>
                  <h3>{s.name}</h3>
                  {s.description && <p>{s.description}</p>}
                </li>
              ))}
            </ul>
          </div>
        </section>
      )}

      {home.cases.length > 0 && (
        <section aria-labelledby="cases" className="sec sec--ink">
          <div className="wrap">
            <div className="sec__head">
              <div>
                <h2 id="cases" className="h2" data-reveal>{c.casesTitle}</h2>
                <p className="lede" data-reveal>{t("site.cases.lede")}</p>
              </div>
              <Link href="/cases" className="textlink">{t("site.cases.all")}</Link>
            </div>
            <HoverReel
              items={home.cases.map((x) => ({
                href: `/cases/${x.slug}`,
                title: x.businessName,
                meta: [x.industry, x.city, x.followerGrowth !== null ? `${t("site.cases.followers")} ${t("site.cases.fromTo", { from: faNum(x.followersBefore ?? 0), to: faNum(x.followersAfter ?? 0) })}` : null].filter(Boolean).join("، "),
                number: x.followerGrowth !== null ? t("site.cases.growth", { n: faNum(x.followerGrowth) }) : null,
                tag: x.isSample ? t("site.cases.sample") : null,
                image: x.coverUrl,
              }))}
            />
          </div>
        </section>
      )}

      {c.process.length > 0 && (
        <section aria-labelledby="process" className="sec sec--sand">
          <div className="wrap grid gap-12 lg:grid-cols-[1fr_1.2fr]">
            <div className="lg:sticky lg:top-32 lg:self-start">
              <h2 id="process" className="h2" data-reveal>{c.processTitle}</h2>
              <p className="lede" data-reveal>{t("site.process.lede")}</p>
            </div>
            <ol className="tl" data-tl>
              <span className="tl__line" aria-hidden="true" />
              {c.process.map((s, i) => (
                <li key={s.title} className="tl__step" data-observe>
                  <span className="tl__dot" aria-hidden="true">{faNum(i + 1)}</span>
                  <h3>{s.title}</h3>
                  <p>{s.body}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>
      )}

      {home.packages.length > 0 && (
        <section aria-labelledby="packages" className="sec" id="packages-list" data-testid="packages-list">
          <div className="wrap">
            <div className="sec__head">
              <div>
                <h2 id="packages" className="h2" data-reveal>{c.packagesTitle}</h2>
                <p className="lede" data-reveal>{t("site.packages.lede")}</p>
              </div>
              {home.packages.length > 3 && <Link href="/plans" className="textlink">{t("site.nav.packages")}</Link>}
            </div>
            <div className="tiers">
              {home.packages.slice(0, 3).map((p, i) => (
                <PackageCard key={p.slug} p={p} t={t} delay={i * 110} />
              ))}
            </div>
          </div>
        </section>
      )}

      {quotes.length > 0 && (
        <section aria-labelledby="quotes" className="sec sec--ink overflow-hidden">
          <div className="wrap">
            <div className="sec__head">
              <div>
                <h2 id="quotes" className="h2" data-reveal>{c.testimonialsTitle}</h2>
                <p className="lede" data-reveal>{t("site.quotes.lede")}</p>
              </div>
            </div>
          </div>
          <div className="quotes">
            {[false, true].map((rev) => (
              <div key={String(rev)} className={`marquee ${rev ? "marquee--rev" : ""}`} style={{ ["--speed" as string]: rev ? "70s" : "60s", ["--gap" as string]: "18px" }}>
                {[0, 1].map((k2) => (
                  <div className="marquee__track" key={k2} aria-hidden={k2 === 1 || rev ? true : undefined}>
                    {(rev ? [...quotes].reverse() : quotes).map((q, i) => (
                      <figure key={`${q.slug}-${i}`} className="quote">
                        <blockquote>«{q.quote}»</blockquote>
                        <figcaption>
                          <span aria-hidden="true">{q.businessName.trim().charAt(0)}</span>
                          <span className="!block !size-auto !bg-transparent !text-inherit !font-normal">
                            {q.by} — <Link href={`/cases/${q.slug}`} className="underline" tabIndex={k2 === 1 || rev ? -1 : undefined}>{q.businessName}</Link>
                          </span>
                        </figcaption>
                      </figure>
                    ))}
                  </div>
                ))}
              </div>
            ))}
          </div>
        </section>
      )}

      {c.showBlog && (posts?.items.length ?? 0) > 0 && (
        <section aria-labelledby="latest" className="sec sec--sand">
          <div className="wrap">
            <div className="sec__head">
              <h2 id="latest" className="h2" data-reveal>{c.blogTitle}</h2>
              <Link href="/blog" className="textlink">{t("site.nav.blog")}</Link>
            </div>
            <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
              {posts!.items.slice(0, 3).map((p) => (
                <PostCard key={p.slug} post={p} t={t} />
              ))}
            </div>
          </div>
        </section>
      )}

      {c.faq.length > 0 && (
        <section aria-labelledby="faq" className="sec">
          <div className="wrap grid gap-10 lg:grid-cols-[1fr_1.4fr]">
            <div>
              <h2 id="faq" className="h2" data-reveal>{c.faqTitle}</h2>
              <p className="lede" data-reveal>{t("site.faq.lede")}</p>
            </div>
            <div className="faq">
              {c.faq.map((f, i) => (
                <details key={f.question} data-reveal style={{ ["--d" as string]: `${i * 70}ms` }}>
                  <summary>
                    {f.question}
                    <i aria-hidden="true">+</i>
                  </summary>
                  <p>{f.answer}</p>
                </details>
              ))}
            </div>
          </div>
        </section>
      )}

      <section id="consult" aria-labelledby="consult-title" className="aurora sec scroll-mt-20">
        <div className="wrap grid items-start gap-12 lg:grid-cols-[1fr_1.15fr]">
          <div>
            <h2 id="consult-title" className="h2" data-reveal>{c.consultTitle}</h2>
            <p className="lede" data-reveal>{c.consultText}</p>
            <address className="mt-10 space-y-3 not-italic">
              <h3 className="text-sm font-bold opacity-60">{t("site.contact.title")}</h3>
              {k.phone && <p><a href={`tel:${k.phone}`} dir="ltr" className="text-3xl font-black text-[var(--hi-text)] hover:underline">{k.phone}</a></p>}
              {(k.address || k.city) && <p className="leading-8 opacity-85">{[k.city, k.address].filter(Boolean).join("، ")}</p>}
              {k.hours && <p className="text-sm opacity-70">{t("site.contact.hours")}: {k.hours}</p>}
              <div className="flex flex-wrap gap-2 pt-2">
                {k.neshanUrl && <a href={k.neshanUrl} target="_blank" rel="noopener noreferrer" className="rounded-full border border-white/25 px-4 py-2 text-sm font-bold hover:bg-white/10">{t("site.contact.neshan")}</a>}
                {k.baladUrl && <a href={k.baladUrl} target="_blank" rel="noopener noreferrer" className="rounded-full border border-white/25 px-4 py-2 text-sm font-bold hover:bg-white/10">{t("site.contact.balad")}</a>}
              </div>
            </address>
          </div>
          <div className="glass" data-reveal>
            <ConsultForm
              source="/"
              texts={{
                name: t("site.form.name"), phone: t("site.form.phone"), business: t("site.form.business"), message: t("site.form.message"),
                send: t("site.form.send"), sending: t("site.form.sending"), sent: t("site.form.sent"), error: t("site.form.error"),
              }}
            />
          </div>
        </div>
      </section>
    </>
  );
}
