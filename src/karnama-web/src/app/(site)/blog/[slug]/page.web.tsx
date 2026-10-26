import type { Metadata } from "next";
import Link from "next/link";
import { notFound, permanentRedirect } from "next/navigation";
import { PostCard } from "@/components/site/post-card";
import { faNum, formatFaDate, getBrand, getT, jsonLd, siteGet, SITE_URL, type Post } from "@/lib/site";

export const revalidate = 60;
type Props = { params: Promise<{ slug: string }> };
type Lookup = Post & { redirectTo?: string };

async function load(slug: string) {
  return siteGet<Lookup>(`/api/v1/public/blog/posts/${encodeURIComponent(decodeURIComponent(slug))}`);
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  const [post, brand] = await Promise.all([load(slug), getBrand()]);
  if (!post || post.redirectTo) return { robots: { index: false } };
  const url = `${SITE_URL}/blog/${post.slug}`;
  const image = post.coverUrl ? `${SITE_URL}${post.coverUrl}` : `${SITE_URL}/og/blog/${post.slug}`;
  return {
    title: { absolute: post.seoTitle },
    description: post.metaDescription,
    keywords: [post.focusKeyword, ...post.tags].filter(Boolean),
    authors: [{ name: post.authorName }],
    alternates: { canonical: url, languages: { fa: url, "x-default": url } },
    robots: { index: true, follow: true, "max-image-preview": "large" },
    openGraph: {
      type: "article", url, title: post.seoTitle, description: post.metaDescription, siteName: brand?.name ?? undefined, locale: "fa_IR",
      publishedTime: post.publishedAt, modifiedTime: post.modifiedAt, authors: [post.authorName], tags: post.tags,
      images: [{ url: image, width: 1200, height: 630, alt: post.coverAlt ?? post.title }],
    },
    twitter: { card: "summary_large_image", title: post.seoTitle, description: post.metaDescription, images: [image] },
  };
}

export default async function PostPage({ params }: Props) {
  const { slug } = await params;
  const [post, t, brand] = await Promise.all([load(slug), getT(), getBrand()]);
  if (!post) notFound();
  if (post.redirectTo) permanentRedirect(`/blog/${encodeURIComponent(post.redirectTo)}`);
  const url = `${SITE_URL}/blog/${post.slug}`;
  const org = { "@type": "Organization", name: brand?.name ?? "کارنما", url: SITE_URL, ...(brand?.logoUrl ? { logo: `${SITE_URL}${brand.logoUrl}` } : {}) };
  const graph = [
    {
      "@type": "BlogPosting",
      "@id": `${url}#article`,
      mainEntityOfPage: url,
      headline: post.title,
      description: post.metaDescription,
      inLanguage: "fa-IR",
      datePublished: post.publishedAt,
      dateModified: post.modifiedAt,
      wordCount: post.wordCount,
      timeRequired: `PT${post.readingMinutes}M`,
      keywords: [post.focusKeyword, ...post.tags].filter(Boolean).join(", "),
      articleSection: post.categoryName ?? undefined,
      image: post.coverUrl ? `${SITE_URL}${post.coverUrl}` : `${SITE_URL}/og/blog/${post.slug}`,
      author: { "@type": "Person", name: post.authorName },
      publisher: org,
    },
    {
      "@type": "BreadcrumbList",
      itemListElement: [
        { "@type": "ListItem", position: 1, name: t("site.nav.home"), item: SITE_URL },
        { "@type": "ListItem", position: 2, name: t("site.blog.title"), item: `${SITE_URL}/blog` },
        ...(post.categoryName ? [{ "@type": "ListItem", position: 3, name: post.categoryName, item: `${SITE_URL}/blog?category=${encodeURIComponent(post.categorySlug!)}` }] : []),
        { "@type": "ListItem", position: post.categoryName ? 4 : 3, name: post.title, item: url },
      ],
    },
    ...(post.faq.length > 0
      ? [{ "@type": "FAQPage", mainEntity: post.faq.map((f) => ({ "@type": "Question", name: f.question, acceptedAnswer: { "@type": "Answer", text: f.answer } })) }]
      : []),
  ];
  const share = encodeURIComponent(url);

  return (
    <>
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLd({ "@context": "https://schema.org", "@graph": graph }) }} />
      <article className="mx-auto max-w-6xl px-4 pt-32 pb-16 sm:pt-36" data-testid="post">
        <nav aria-label={t("site.blog.breadcrumb")} className="text-sm text-muted-foreground">
          <ol className="flex flex-wrap items-center gap-2">
            <li><Link href="/" className="hover:underline">{t("site.nav.home")}</Link></li>
            <li aria-hidden="true">/</li>
            <li><Link href="/blog" className="hover:underline">{t("site.blog.title")}</Link></li>
            {post.categoryName && (
              <>
                <li aria-hidden="true">/</li>
                <li><Link href={`/blog?category=${encodeURIComponent(post.categorySlug!)}`} className="hover:underline">{post.categoryName}</Link></li>
              </>
            )}
          </ol>
        </nav>

        <header className="mt-6 max-w-3xl">
          <h1 className="text-3xl leading-[1.5] font-black sm:text-5xl sm:leading-[1.35]">{post.title}</h1>
          <p className="mt-5 flex flex-wrap gap-x-4 gap-y-1 text-sm text-muted-foreground">
            <span>{t("site.blog.by", { name: post.authorName })}</span>
            <time dateTime={post.publishedAt}>{formatFaDate(post.publishedAt)}</time>
            <span>{t("site.blog.minutes", { n: faNum(post.readingMinutes) })}</span>
            {post.modifiedAt.slice(0, 10) !== post.publishedAt.slice(0, 10) && <span>{t("site.blog.updated", { date: formatFaDate(post.modifiedAt) })}</span>}
          </p>
        </header>

        {post.coverUrl && (
          // eslint-disable-next-line @next/next/no-img-element -- resized WebP from the editor; fixed ratio, no layout shift
          <img src={post.coverUrl} alt={post.coverAlt ?? post.title} width={1600} height={900} fetchPriority="high" className="mt-8 aspect-video w-full rounded-2xl object-cover" />
        )}

        <div className="mt-10 grid gap-10 lg:grid-cols-[minmax(0,1fr)_16rem]">
          <div className="min-w-0">
            {post.toc.length >= 2 && (
              <details className="mb-8 rounded-xl border bg-card p-4 lg:hidden" open>
                <summary className="cursor-pointer font-bold">{t("site.blog.toc")}</summary>
                <Toc toc={post.toc} />
              </details>
            )}
            {/* Rendered and whitelisted on the server from the editor's JSON (TiptapRenderer). */}
            <div className="prose-fa" dangerouslySetInnerHTML={{ __html: post.html }} />

            {post.tags.length > 0 && (
              <ul aria-label={t("site.blog.tags")} className="mt-10 flex flex-wrap gap-2">
                {post.tags.map((tag) => (
                  <li key={tag} className="rounded-full bg-muted px-3 py-1 text-sm">#{tag}</li>
                ))}
              </ul>
            )}
            <div className="mt-8 flex flex-wrap items-center gap-3 border-t pt-6 text-sm">
              <span className="font-bold">{t("site.blog.share")}</span>
              <a className="rounded-md border px-3 py-2 hover:bg-muted" href={`https://t.me/share/url?url=${share}&text=${encodeURIComponent(post.title)}`} target="_blank" rel="noopener noreferrer">
                {t("site.blog.shareTelegram")}
              </a>
              <a className="rounded-md border px-3 py-2 hover:bg-muted" href={`https://wa.me/?text=${encodeURIComponent(post.title + " " + url)}`} target="_blank" rel="noopener noreferrer">
                {t("site.blog.shareWhatsapp")}
              </a>
            </div>
          </div>
          {post.toc.length >= 2 && (
            <aside className="hidden lg:block">
              <div className="sticky top-6 rounded-xl border bg-card p-4">
                <p className="font-bold">{t("site.blog.toc")}</p>
                <Toc toc={post.toc} />
              </div>
            </aside>
          )}
        </div>

        {post.related.length > 0 && (
          <section aria-labelledby="related" className="mt-16">
            <h2 id="related" className="text-2xl font-extrabold">{t("site.blog.related")}</h2>
            <div className="mt-6 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
              {post.related.map((p) => (
                <PostCard key={p.slug} post={p} t={t} />
              ))}
            </div>
          </section>
        )}
      </article>
    </>
  );
}

function Toc({ toc }: { toc: Post["toc"] }) {
  return (
    <ol className="mt-3 space-y-2 text-sm">
      {toc.map((h) => (
        <li key={h.id} className={h.level === 3 ? "ps-4" : ""}>
          <a href={`#${encodeURIComponent(h.id)}`} className="leading-6 text-muted-foreground hover:text-foreground hover:underline">{h.text}</a>
        </li>
      ))}
    </ol>
  );
}
