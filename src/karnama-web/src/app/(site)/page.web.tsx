import type { Metadata } from "next";
import Link from "next/link";
import { PostCard } from "@/components/site/post-card";
import { getBrand, getT, siteGet, SITE_URL, type BlogList } from "@/lib/site";

export const revalidate = 60;

export async function generateMetadata(): Promise<Metadata> {
  const brand = await getBrand();
  const name = brand?.name ?? "کارنما";
  return {
    title: { absolute: brand?.tagline ? `${name} | ${brand.tagline}` : name },
    description: brand?.tagline ?? undefined,
    alternates: { canonical: `${SITE_URL}/` },
    robots: { index: true, follow: true },
  };
}

/** Home page (N10 grows this: services, case studies, packages, FAQ, consultation form — all editable). */
export default async function Home() {
  const [brand, t, posts] = await Promise.all([getBrand(), getT(), siteGet<BlogList>("/api/v1/public/blog?page=1")]);
  return (
    <div className="mx-auto max-w-6xl px-4 py-16">
      <h1 className="max-w-3xl text-4xl leading-[1.4] font-black sm:text-6xl sm:leading-[1.3]">{brand?.tagline ?? brand?.name ?? "کارنما"}</h1>
      {(posts?.items.length ?? 0) > 0 && (
        <section className="mt-16" aria-labelledby="latest">
          <div className="flex items-end justify-between">
            <h2 id="latest" className="text-2xl font-extrabold">{t("site.blog.title")}</h2>
            <Link href="/blog" className="font-semibold underline underline-offset-4">{t("site.blog.readMore")}</Link>
          </div>
          <div className="mt-6 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {posts!.items.slice(0, 3).map((p) => (
              <PostCard key={p.slug} post={p} t={t} />
            ))}
          </div>
        </section>
      )}
    </div>
  );
}
