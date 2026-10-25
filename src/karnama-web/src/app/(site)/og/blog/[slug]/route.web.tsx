import { ogCard } from "@/components/site/og-card";
import { getBrand, getT, siteGet, type Post } from "@/lib/site";

export const revalidate = 3600;

/** Automatic share image of a post: its title on the brand template (used when the post has no cover photo). */
export async function GET(_: Request, { params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const [post, brand, t] = await Promise.all([siteGet<Post>(`/api/v1/public/blog/posts/${encodeURIComponent(decodeURIComponent(slug))}`), getBrand(), getT()]);
  const res = await ogCard(post?.title ?? t("site.blog.title"), brand, post?.categoryName ?? t("site.blog.title"));
  res.headers.set("Cache-Control", "public, max-age=3600, s-maxage=3600");
  return res;
}
