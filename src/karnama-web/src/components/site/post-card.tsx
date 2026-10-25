import Link from "next/link";
import type { Card, T } from "@/lib/site";
import { faNum, formatFaDate } from "@/lib/site";

export function PostCard({ post, t, eager = false }: { post: Card; t: T; eager?: boolean }) {
  return (
    <article className="group flex flex-col overflow-hidden rounded-2xl border border-border bg-card transition-shadow hover:shadow-md">
      {post.coverUrl ? (
        // eslint-disable-next-line @next/next/no-img-element -- images are already resized WebP from the editor
        <img src={post.coverUrl} alt={post.coverAlt ?? ""} width={800} height={450} loading={eager ? "eager" : "lazy"} decoding="async" className="aspect-video w-full object-cover" />
      ) : (
        <div aria-hidden="true" className="relative aspect-video w-full bg-[var(--brand-black)]">
          <span className="absolute inset-0 grid place-items-center px-6 text-center text-lg font-extrabold leading-8 text-[var(--brand-yellow)]">{post.title}</span>
        </div>
      )}
      <div className="flex flex-1 flex-col gap-3 p-5">
        {post.categoryName && (
          <Link href={`/blog?category=${encodeURIComponent(post.categorySlug!)}`} className="w-fit rounded-full bg-muted px-3 py-1 text-xs font-semibold hover:bg-foreground hover:text-background">
            {post.categoryName}
          </Link>
        )}
        <h2 className="text-lg leading-8 font-extrabold">
          <Link href={`/blog/${post.slug}`} className="hover:underline">
            {post.title}
          </Link>
        </h2>
        <p className="line-clamp-3 text-sm leading-7 text-muted-foreground">{post.excerpt}</p>
        <p className="mt-auto text-xs text-muted-foreground">
          <time dateTime={post.publishedAt}>{formatFaDate(post.publishedAt)}</time> · {t("site.blog.minutes", { n: faNum(post.readingMinutes) })}
        </p>
      </div>
    </article>
  );
}
