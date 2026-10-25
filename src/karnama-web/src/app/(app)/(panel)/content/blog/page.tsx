"use client";

import { Plus } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faBlog } from "@/lib/i18n/fa-blog";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useKeysetList } from "@/lib/use-keyset";
import { cn } from "@/lib/utils";
import { scoreTone, statusTone, type PostListItem, type PostStatus } from "@/components/blog/types";

registerTexts(faBlog);
const FILTERS: (PostStatus | "")[] = ["", "draft", "inReview", "scheduled", "published"];

export default function BlogListPage() {
  const allowed = useRequire([{ code: "blog.write", scope: "own" }, { code: "blog.publish", scope: "all" }]);
  const { t, formatNumber, formatDate } = useI18n();
  const router = useRouter();
  const errorMessage = useErrorMessage();
  const [status, setStatus] = useState<PostStatus | "">("");
  const [creating, setCreating] = useState(false);
  const list = useKeysetList<PostListItem>("blog-posts", "/api/v1/blog/posts", { status: status || undefined, limit: "30" }, allowed);
  if (!allowed) return null;

  const create = async () => {
    setCreating(true);
    try {
      const doc = JSON.stringify({ type: "doc", content: [{ type: "paragraph" }] });
      const p = await api<{ id: string }>("/api/v1/blog/posts", { method: "POST", body: { title: t("blog.new"), contentJson: doc } });
      router.push(`/content/blog/edit?id=${p.id}`);
    } catch (e) {
      notify.error(errorMessage(e));
      setCreating(false);
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="max-w-2xl">
          <h1 className="text-2xl font-extrabold">{t("blog.title")}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t("blog.subtitle")}</p>
        </div>
        <Button className="h-12" onClick={create} disabled={creating} data-testid="blog-new">
          <Plus aria-hidden="true" />
          {t("blog.new")}
        </Button>
      </header>
      <div className="flex flex-wrap gap-2" role="tablist">
        {FILTERS.map((f) => (
          <button
            key={f || "all"}
            role="tab"
            aria-selected={status === f}
            onClick={() => setStatus(f)}
            className={cn("min-h-10 rounded-full border-2 px-4 text-sm font-semibold", status === f ? "border-foreground bg-foreground text-background" : "border-border hover:border-foreground")}
          >
            {f ? t(`blog.status.${f}` as TextKey) : t("blog.filter.all")}
          </button>
        ))}
      </div>
      {list.isLoading && <div className="h-40 animate-pulse rounded-xl bg-muted" />}
      {!list.isLoading && list.items.length === 0 && (
        <p className="rounded-xl border border-dashed p-10 text-center text-muted-foreground">{t("blog.empty")}</p>
      )}
      <ul className="divide-y rounded-xl border bg-card" data-testid="blog-posts">
        {list.items.map((p) => (
          <li key={p.id}>
            <Link href={`/content/blog/edit?id=${p.id}`} className="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-3 hover:bg-muted/50">
              <span className="min-w-0 flex-1 basis-64 font-semibold">{p.title}</span>
              <span className={cn("rounded-full px-2.5 py-0.5 text-xs font-semibold", statusTone[p.status])}>{t(`blog.status.${p.status}` as TextKey)}</span>
              <span className={cn("text-sm font-bold tabular-nums", scoreTone(p.seoScore))} title={t("blog.score")}>
                {formatNumber(p.seoScore)}٪
              </span>
              <span className="text-xs text-muted-foreground">
                {p.authorName} · {t("blog.words", { n: formatNumber(p.wordCount) })} · {formatDate(p.publishedAt ?? p.modifiedAt, { day: "numeric", month: "short" })}
              </span>
            </Link>
          </li>
        ))}
      </ul>
      {list.hasMore && (
        <Button variant="outline" className="h-11" onClick={list.loadMore} disabled={list.loadingMore}>
          {t("common.loadMore")}
        </Button>
      )}
    </div>
  );
}
