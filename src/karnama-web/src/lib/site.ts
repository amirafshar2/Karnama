import type { Brand } from "./brand";
import { faSite } from "./i18n/fa-site";

/**
 * Server-side reads for the public site (SSR/ISR). The API is called from the Next server, so the browser gets finished
 * HTML; `revalidate` keeps pages fresh without hitting the API on every visit.
 */
const API = (process.env.API_INTERNAL_BASE ?? process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:5080").replace(/\/$/, "");
export const SITE_URL = (process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000").replace(/\/$/, "");

export async function siteGet<T>(path: string, revalidate = 60): Promise<T | null> {
  try {
    const r = await fetch(`${API}${path}`, { next: { revalidate }, headers: { Accept: "application/json" } });
    if (r.status === 404) return null;
    if (!r.ok) throw new Error(`API ${r.status} for ${path}`);
    return (await r.json()) as T;
  } catch (e) {
    console.error("[site]", path, (e as Error).message);
    return null;
  }
}

export const getBrand = () => siteGet<Brand>("/api/v1/public/brand", 300);

export type TocItem = { level: number; id: string; text: string };
export type Card = { title: string; slug: string; excerpt: string; coverUrl: string | null; coverAlt: string | null; categoryName: string | null; categorySlug: string | null; authorName: string; publishedAt: string; readingMinutes: number };
export type Category = { id: string; name: string; slug: string; description: string | null; sortOrder: number; posts: number };
export type BlogList = { items: Card[]; page: number; pages: number; categories: Category[] };
export type Post = {
  id: string; title: string; slug: string; html: string; toc: TocItem[]; faq: { question: string; answer: string }[];
  seoTitle: string; metaDescription: string; focusKeyword: string; coverUrl: string | null; coverAlt: string | null;
  categoryName: string | null; categorySlug: string | null; tags: string[]; authorName: string; publishedAt: string; modifiedAt: string;
  readingMinutes: number; wordCount: number; related: Card[];
};

/** JSON-LD inside <script type="application/ld+json">: "<" is escaped so content can never close the tag. */
export const jsonLd = (data: unknown) => JSON.stringify(data).replace(/</g, "\\u003c");

const faDate = new Intl.DateTimeFormat("fa-IR-u-ca-persian", { day: "numeric", month: "long", year: "numeric", timeZone: "Asia/Tehran" });
export const formatFaDate = (iso: string) => faDate.format(new Date(iso));
export const faNum = (n: number) => n.toLocaleString("fa-IR");

export type SiteKey = keyof typeof faSite;
export type T = (key: SiteKey, vars?: Record<string, string | number>) => string;

/** Server-side texts: the admin's edits (Settings › Texts) over the built-in Persian. */
export async function getT(): Promise<T> {
  const bundle = await siteGet<{ texts: Record<string, string> }>("/api/v1/i18n/fa", 60);
  const over = bundle?.texts ?? {};
  return (key, vars) => {
    let s = over[key] ?? faSite[key] ?? key;
    if (vars) for (const [k, v] of Object.entries(vars)) s = s.replaceAll(`{${k}}`, String(v));
    return s;
  };
}
