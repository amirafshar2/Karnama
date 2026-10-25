import type { Metadata } from "next";
import Link from "next/link";
import { PostCard } from "@/components/site/post-card";
import { faNum, getBrand, getT, siteGet, SITE_URL, type BlogList } from "@/lib/site";
import { cn } from "@/lib/utils";

export const revalidate = 60;
type Props = { searchParams: Promise<{ page?: string; category?: string }> };

export async function generateMetadata({ searchParams }: Props): Promise<Metadata> {
  const [{ page, category }, t, brand] = await Promise.all([searchParams, getT(), getBrand()]);
  const name = brand?.name ?? "کارنما";
  const p = Number(page) > 1 ? Number(page) : 1;
  const qs = new URLSearchParams({ ...(category ? { category } : {}), ...(p > 1 ? { page: String(p) } : {}) }).toString();
  const url = `${SITE_URL}/blog${qs ? `?${qs}` : ""}`;
  return {
    title: { absolute: `${t("site.blog.title")}${p > 1 ? ` — ${t("site.blog.page", { n: faNum(p) })}` : ""} | ${name}` },
    description: t("site.blog.description"),
    alternates: { canonical: url },
    robots: { index: true, follow: true },
    openGraph: { type: "website", url, title: `${t("site.blog.title")} | ${name}`, description: t("site.blog.description"), siteName: name, locale: "fa_IR" },
  };
}

export default async function BlogIndex({ searchParams }: Props) {
  const { page, category } = await searchParams;
  const qs = new URLSearchParams({ page: String(Math.max(1, Number(page) || 1)), ...(category ? { category } : {}) });
  const [data, t] = await Promise.all([siteGet<BlogList>(`/api/v1/public/blog?${qs}`), getT()]);
  const list = data ?? { items: [], page: 1, pages: 1, categories: [] };
  const link = (p: number) => `/blog?${new URLSearchParams({ ...(category ? { category } : {}), ...(p > 1 ? { page: String(p) } : {}) })}`;

  return (
    <div className="mx-auto max-w-6xl px-4 py-12">
      <header className="max-w-2xl">
        <h1 className="text-4xl font-black tracking-tight sm:text-5xl">{t("site.blog.title")}</h1>
        <p className="mt-4 text-lg leading-8 text-muted-foreground">{t("site.blog.description")}</p>
      </header>

      {list.categories.length > 0 && (
        <nav aria-label={t("site.blog.title")} className="mt-8 flex flex-wrap gap-2">
          <Link href="/blog" aria-current={!category ? "page" : undefined} className={cn("rounded-full border-2 px-4 py-2 text-sm font-semibold", !category ? "border-foreground bg-foreground text-background" : "border-border hover:border-foreground")}>
            {t("site.blog.all")}
          </Link>
          {list.categories.map((c) => (
            <Link
              key={c.id}
              href={`/blog?category=${encodeURIComponent(c.slug)}`}
              aria-current={category === c.slug ? "page" : undefined}
              className={cn("rounded-full border-2 px-4 py-2 text-sm font-semibold", category === c.slug ? "border-foreground bg-foreground text-background" : "border-border hover:border-foreground")}
            >
              {c.name} <span className="opacity-60">{faNum(c.posts)}</span>
            </Link>
          ))}
        </nav>
      )}

      {list.items.length === 0 ? (
        <p className="mt-16 rounded-2xl border border-dashed p-10 text-center text-muted-foreground">{t("site.blog.empty")}</p>
      ) : (
        <div className="mt-10 grid gap-6 sm:grid-cols-2 lg:grid-cols-3" data-testid="blog-list">
          {list.items.map((p, i) => (
            <PostCard key={p.slug} post={p} t={t} eager={i < 3} />
          ))}
        </div>
      )}

      {list.pages > 1 && (
        <nav aria-label={t("site.blog.page", { n: "" })} className="mt-12 flex items-center justify-center gap-3">
          {list.page > 1 && (
            <Link rel="prev" href={link(list.page - 1)} className="rounded-md border-2 border-foreground px-4 py-2 font-semibold">
              {t("site.blog.prev")}
            </Link>
          )}
          <span className="text-sm text-muted-foreground">
            {faNum(list.page)} / {faNum(list.pages)}
          </span>
          {list.page < list.pages && (
            <Link rel="next" href={link(list.page + 1)} className="rounded-md border-2 border-foreground px-4 py-2 font-semibold">
              {t("site.blog.next")}
            </Link>
          )}
        </nav>
      )}
    </div>
  );
}
