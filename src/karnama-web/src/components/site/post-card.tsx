import Link from "next/link";
import type { Card, T } from "@/lib/site";
import { faNum, formatFaDate } from "@/lib/site";

/** Blog card: the cover slowly zooms on hover; a cover-less post gets its title set large on the brand black. */
export function PostCard({ post, t, eager = false }: { post: Card; t: T; eager?: boolean }) {
  return (
    <article className="post" data-reveal>
      <Link href={`/blog/${post.slug}`} className="post__cover block" tabIndex={-1} aria-hidden="true">
        {post.coverUrl ? (
          // eslint-disable-next-line @next/next/no-img-element -- images are already resized WebP from the editor
          <img src={post.coverUrl} alt="" width={800} height={500} loading={eager ? "eager" : "lazy"} decoding="async" />
        ) : (
          <span className="relative grid size-full place-items-center bg-[var(--ink)] px-6 text-center text-xl font-black leading-9 text-[var(--hi-text)]">{post.title}</span>
        )}
      </Link>
      <div className="flex flex-1 flex-col gap-3 p-6">
        {post.categoryName && (
          <Link href={`/blog?category=${encodeURIComponent(post.categorySlug!)}`} className="w-fit rounded-full bg-[var(--hi)] px-3 py-1 text-xs font-bold text-[var(--hi-fg)]">
            {post.categoryName}
          </Link>
        )}
        <h2 className="text-lg leading-8 font-black">
          <Link href={`/blog/${post.slug}`} className="hover:underline">
            {post.title}
          </Link>
        </h2>
        <p className="line-clamp-3 text-sm leading-7 opacity-70">{post.excerpt}</p>
        <p className="mt-auto text-xs opacity-60">
          <time dateTime={post.publishedAt}>{formatFaDate(post.publishedAt)}</time> · {t("site.blog.minutes", { n: faNum(post.readingMinutes) })}
        </p>
      </div>
    </article>
  );
}
