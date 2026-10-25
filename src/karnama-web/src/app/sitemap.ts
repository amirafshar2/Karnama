import type { MetadataRoute } from "next";
import { siteGet, SITE_URL } from "@/lib/site";

export const revalidate = 60;
export const dynamic = "force-static";

/** Every public URL with its last change (posts from the API; static pages here). */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const entries = (await siteGet<{ path: string; modified: string }[]>("/api/v1/public/blog/sitemap", 60)) ?? [];
  const now = new Date();
  return [
    { url: `${SITE_URL}/`, lastModified: now, changeFrequency: "weekly", priority: 1 },
    ...entries.map((e) => ({
      url: `${SITE_URL}${e.path}`,
      lastModified: new Date(e.modified),
      changeFrequency: e.path.startsWith("/blog/") ? ("monthly" as const) : ("weekly" as const),
      priority: e.path.startsWith("/blog/") ? 0.7 : 0.6,
    })),
  ];
}
