import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { faNum, getBrand, getT, jsonLd, siteGet, SITE_URL, toman, type CaseDetail } from "@/lib/site";

export const revalidate = 60;
type Props = { params: Promise<{ slug: string }> };
const load = (slug: string) => siteGet<CaseDetail>(`/api/v1/public/site/cases/${encodeURIComponent(decodeURIComponent(slug))}`);
const faDate = (d: string) => new Intl.DateTimeFormat("fa-IR-u-ca-persian", { year: "numeric", month: "long", day: "numeric" }).format(new Date(d));
const faMonth = (d: string) => new Intl.DateTimeFormat("fa-IR-u-ca-persian", { year: "numeric", month: "long" }).format(new Date(d));

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  const [c, brand, t] = await Promise.all([load(slug), getBrand(), getT()]);
  if (!c) return { robots: { index: false } };
  const title = `${c.card.businessName} — ${t("site.cases.title")} | ${brand?.name ?? "کارنما"}`;
  const description = c.card.summary ?? undefined;
  return { title: { absolute: title }, description, alternates: { canonical: `${SITE_URL}/cases/${c.card.slug}` }, robots: { index: !c.card.isSample, follow: true } };
}

export default async function CasePage({ params }: Props) {
  const { slug } = await params;
  const [c, t] = await Promise.all([load(slug), getT()]);
  if (!c) notFound();
  const x = c.card;
  const graph = {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: [
      { "@type": "ListItem", position: 1, name: t("site.nav.home"), item: SITE_URL },
      { "@type": "ListItem", position: 2, name: t("site.cases.title"), item: `${SITE_URL}/cases` },
      { "@type": "ListItem", position: 3, name: x.businessName, item: `${SITE_URL}/cases/${x.slug}` },
    ],
  };
  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLd(graph) }} />
      <article className="mx-auto max-w-5xl px-4 pt-32 pb-16 sm:pt-36" data-testid="case">
        <nav aria-label={t("site.blog.breadcrumb")} className="text-sm text-muted-foreground">
          <Link href="/" className="hover:underline">{t("site.nav.home")}</Link> / <Link href="/cases" className="hover:underline">{t("site.cases.title")}</Link>
        </nav>
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <h1 className="text-4xl font-black sm:text-5xl">{x.businessName}</h1>
          {x.isSample && <span className="rounded-full bg-muted px-3 py-1 text-sm font-bold">{t("site.cases.sample")}</span>}
        </div>
        <p className="mt-3 text-muted-foreground">
          {[x.industry, x.city, c.handle && `@${c.handle}`].filter(Boolean).join(" · ")}
          {c.from && ` · ${t("site.cases.period")}: ${faMonth(c.from)}${c.to ? ` – ${faMonth(c.to)}` : ""}`}
        </p>

        <div className="mt-10 grid gap-4 sm:grid-cols-2">
          {x.followerGrowth !== null && (
            <div className="rounded-2xl bg-[var(--brand-black)] p-6 text-[#f4f4ef]">
              <p className="text-sm opacity-75">{t("site.cases.followers")}</p>
              <p className="mt-1 text-5xl font-black text-[var(--brand-on-dark)]">{t("site.cases.growth", { n: faNum(x.followerGrowth) })}</p>
              <p className="mt-2 text-sm">{t("site.cases.fromTo", { from: faNum(x.followersBefore ?? 0), to: faNum(x.followersAfter ?? 0) })}</p>
            </div>
          )}
          {x.revenueGrowth !== null && (
            <div className="rounded-2xl border border-black/10 bg-white p-6">
              <p className="text-sm text-muted-foreground">{t("site.cases.revenue")}</p>
              <p className="mt-1 text-5xl font-black">{t("site.cases.growth", { n: faNum(x.revenueGrowth) })}</p>
              {!c.revenueAsPercent && c.revenueBefore !== null && c.revenueAfter !== null && (
                <p className="mt-2 text-sm">{t("site.cases.fromTo", { from: toman(c.revenueBefore), to: toman(c.revenueAfter) })}</p>
              )}
            </div>
          )}
        </div>
        {x.source && <p className="mt-3 text-xs text-muted-foreground">{t("site.cases.source", { source: x.source })}</p>}

        {x.summary && <p className="mt-10 max-w-3xl text-lg leading-9">{x.summary}</p>}
        {x.services.length > 0 && (
          <p className="mt-6 flex flex-wrap gap-2">
            {x.services.map((s) => <span key={s} className="rounded-full bg-muted px-3 py-1 text-sm">{s}</span>)}
          </p>
        )}

        {c.metrics.length > 0 && (
          <section className="mt-12">
            <h2 className="text-2xl font-extrabold">{t("site.cases.metrics")}</h2>
            <div className="mt-4 overflow-x-auto">
              <table className="w-full text-start text-sm">
                <thead><tr className="border-b"><th className="py-2 text-start" /><th className="py-2 text-start">{t("site.cases.before")}</th><th className="py-2 text-start">{t("site.cases.after")}</th></tr></thead>
                <tbody>
                  {c.metrics.map((m) => (
                    <tr key={m.label} className="border-b">
                      <td className="py-3 font-semibold">{m.label}</td>
                      <td className="py-3 tabular-nums">{m.before !== null ? faNum(m.before) : "—"} {m.unit}</td>
                      <td className="py-3 font-bold tabular-nums">{m.after !== null ? faNum(m.after) : "—"} {m.unit}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        )}

        {c.shots.length > 0 && (
          <section className="mt-12">
            <h2 className="text-2xl font-extrabold">{t("site.cases.shots")}</h2>
            <div className="mt-4 grid gap-4 sm:grid-cols-2">
              {c.shots.map((s) => (
                <figure key={s.url} className="overflow-hidden rounded-xl border bg-card">
                  {/* eslint-disable-next-line @next/next/no-img-element -- uploaded screenshots (already WebP) */}
                  <img src={s.url} alt={s.caption ?? x.businessName} loading="lazy" className="w-full" />
                  <figcaption className="p-3 text-xs text-muted-foreground">
                    <span className="font-bold">{s.isAfter ? t("site.cases.after") : t("site.cases.before")}</span>
                    {s.caption && ` — ${s.caption}`}
                    {s.takenOn && ` · ${t("site.cases.takenOn", { date: faDate(s.takenOn) })}`}
                  </figcaption>
                </figure>
              ))}
            </div>
          </section>
        )}

        {c.quote && (
          <figure className="mt-12 rounded-2xl border-s-8 border-primary bg-card p-8">
            <blockquote className="text-2xl leading-[1.8] font-bold">«{c.quote}»</blockquote>
            {c.quoteBy && <figcaption className="mt-4 text-muted-foreground">{c.quoteBy}</figcaption>}
          </figure>
        )}
        <Link href="/#consult" className="mt-12 inline-grid h-14 place-items-center rounded-full bg-primary px-8 font-extrabold text-primary-foreground">
          {t("site.form.send")}
        </Link>
      </article>
    </>
  );
}
