export type PostStatus = "draft" | "inReview" | "scheduled" | "published" | "archived";
export type SeoCheck = { key: string; level: "good" | "warn" | "bad"; detail: string | null };
export type TocItem = { level: number; id: string; text: string };
export type PostListItem = { id: string; title: string; slug: string; status: PostStatus; authorId: string; authorName: string; categoryName: string | null; seoScore: number; wordCount: number; publishedAt: string | null; publishAt: string | null; modifiedAt: string };
export type PostDetail = {
  id: string; title: string; slug: string; status: PostStatus; authorId: string; authorName: string; categoryId: string | null; tags: string[];
  contentJson: string; coverImage: string | null; coverUrl: string | null; coverAlt: string | null;
  seoTitle: string; seoTitleLocked: boolean; metaDescription: string; metaLocked: boolean; focusKeyword: string; keywordLocked: boolean; slugLocked: boolean;
  toc: TocItem[]; seoChecks: SeoCheck[]; seoScore: number; wordCount: number; readingMinutes: number;
  publishAt: string | null; publishedAt: string | null; modifiedAt: string; version: number; canEdit: boolean; canPublish: boolean;
};
export type SeoPreview = { seoTitle: string; metaDescription: string; slug: string; focusKeyword: string; checks: SeoCheck[]; score: number; wordCount: number; readingMinutes: number; toc: TocItem[]; altFilled: number; links: { title: string; url: string; reason: string }[] };
export type Category = { id: string; name: string; slug: string; description: string | null; sortOrder: number; posts: number };

export const statusTone: Record<PostStatus, string> = {
  draft: "bg-muted text-foreground",
  inReview: "bg-amber-100 text-amber-900 dark:bg-amber-950 dark:text-amber-200",
  scheduled: "bg-sky-100 text-sky-900 dark:bg-sky-950 dark:text-sky-200",
  published: "bg-emerald-100 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-200",
  archived: "bg-muted text-muted-foreground",
};
export const scoreTone = (s: number) => (s >= 75 ? "text-emerald-700 dark:text-emerald-400" : s >= 45 ? "text-amber-700 dark:text-amber-400" : "text-destructive");
