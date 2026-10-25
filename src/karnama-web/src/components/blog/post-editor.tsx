"use client";

import { ArrowRight, CalendarClock, Check, ExternalLink, ImagePlus, Loader2, Send, Undo2, X } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState } from "react";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api, ApiError } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQuery } from "@/lib/query";
import { cn } from "@/lib/utils";
import { atTehran } from "@/components/publishing/types";
import { uploadBlogImage } from "./image-prep";
import { EditorContent, EditorToolbar, ImageSettings, useRichEditor } from "./rich-editor";
import { scoreTone, statusTone, type Category, type PostDetail, type SeoPreview } from "./types";

type Fields = { title: string; categoryId: string; tags: string; coverImage: string | null; coverUrl: string | null; coverAlt: string; seoTitle: string; metaDescription: string; focusKeyword: string; slug: string };

const fieldsOf = (p: PostDetail): Fields => ({
  title: p.title, categoryId: p.categoryId ?? "", tags: p.tags.join("، "), coverImage: p.coverImage, coverUrl: p.coverUrl, coverAlt: p.coverAlt ?? "",
  seoTitle: p.seoTitleLocked ? p.seoTitle : "", metaDescription: p.metaLocked ? p.metaDescription : "", focusKeyword: p.keywordLocked ? p.focusKeyword : "", slug: p.slugLocked ? p.slug : "",
});

export function PostEditor({ id }: { id: string }) {
  const post = useQuery({ queryKey: ["blog-post", id], queryFn: () => api<PostDetail>(`/api/v1/blog/posts/${id}`), staleTime: Infinity });
  if (post.error) return <ErrorText error={post.error} />;
  if (!post.data) return <div className="h-96 animate-pulse rounded-xl bg-muted" />;
  return <EditorForm initial={post.data} />;
}

function EditorForm({ initial }: { initial: PostDetail }) {
  const { t, formatNumber, formatDate } = useI18n();
  const router = useRouter();
  const errorMessage = useErrorMessage();
  const [post, setPost] = useState(initial);
  const [f, setF] = useState<Fields>(() => fieldsOf(initial));
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [seo, setSeo] = useState<SeoPreview | null>(null);
  const [schedule, setSchedule] = useState<{ day: string; time: string } | null>(null);
  const [coverBusy, setCoverBusy] = useState(false);
  const [tick, setTick] = useState(0);
  const cats = useQuery({ queryKey: ["blog-categories"], queryFn: () => api<Category[]>("/api/v1/blog/categories") });
  const readOnly = !post.canEdit;

  const touch = useCallback(() => {
    setDirty(true);
    setTick((n) => n + 1);
  }, []);
  const editor = useRichEditor(initial.contentJson, touch, t("editor.bodyPlaceholder"));
  useEffect(() => editor?.setEditable(!readOnly), [editor, readOnly]);
  const set = (patch: Partial<Fields>) => {
    setF((x) => ({ ...x, ...patch }));
    touch();
  };

  // Live SEO: the same server code that runs on save, a second after typing stops.
  const latest = useRef({ f, editor });
  useEffect(() => {
    latest.current = { f, editor };
  });
  useEffect(() => {
    const h = window.setTimeout(async () => {
      const { f: cur, editor: ed } = latest.current;
      if (!ed) return;
      try {
        setSeo(
          await api<SeoPreview>("/api/v1/blog/seo-preview", {
            method: "POST",
            body: { postId: initial.id, title: cur.title, contentJson: JSON.stringify(ed.getJSON()), seoTitle: cur.seoTitle, metaDescription: cur.metaDescription, focusKeyword: cur.focusKeyword },
          }),
        );
      } catch {
        /* preview is best-effort */
      }
    }, tick === 0 ? 0 : 900);
    return () => window.clearTimeout(h);
  }, [tick, initial.id, editor]);

  useEffect(() => {
    const warn = (e: BeforeUnloadEvent) => {
      if (dirty) e.preventDefault();
    };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  const save = async (): Promise<PostDetail | null> => {
    if (!editor) return null;
    setBusy(true);
    setError(null);
    try {
      const saved = await api<PostDetail>(`/api/v1/blog/posts/${post.id}`, {
        method: "PUT",
        body: {
          title: f.title, contentJson: JSON.stringify(editor.getJSON()), categoryId: f.categoryId || null, tags: f.tags.split(/[,،\s]+/).filter(Boolean),
          coverImage: f.coverImage, coverAlt: f.coverAlt, seoTitle: f.seoTitle, metaDescription: f.metaDescription, focusKeyword: f.focusKeyword, slug: f.slug, version: post.version,
        },
      });
      setPost(saved);
      setDirty(false);
      notify.success(t("editor.saved"));
      return saved;
    } catch (e) {
      setError(e);
      if (e instanceof ApiError && e.status === 409) notify.error(errorMessage(e));
      return null;
    } finally {
      setBusy(false);
    }
  };

  const saveRef = useRef(save);
  useEffect(() => {
    saveRef.current = save;
  });
  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "s") {
        e.preventDefault();
        void saveRef.current();
      }
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, []);

  const act = async (path: string, body: unknown, done: string) => {
    const cur = dirty ? await save() : post;
    if (!cur) return;
    setBusy(true);
    try {
      const r = await api<PostDetail>(`/api/v1/blog/posts/${cur.id}/${path}`, { method: "POST", body });
      setPost(r);
      notify.success(done);
      setSchedule(null);
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  const archive = async () => {
    try {
      await api(`/api/v1/blog/posts/${post.id}`, { method: "DELETE" });
      notify.success(t("editor.archived"));
      router.push("/content/blog");
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  const pickCover = async (file: File | undefined) => {
    if (!file) return;
    setCoverBusy(true);
    try {
      const img = await uploadBlogImage(file);
      set({ coverImage: img.id, coverUrl: img.url });
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setCoverBusy(false);
    }
  };

  const insertLink = (url: string, title: string) => {
    if (!editor) return;
    if (editor.state.selection.empty) editor.chain().focus().insertContent({ type: "text", text: title, marks: [{ type: "link", attrs: { href: url } }] }).run();
    else editor.chain().focus().setLink({ href: url }).run();
  };

  const s = seo;
  const effective = { seoTitle: f.seoTitle || s?.seoTitle || post.seoTitle, meta: f.metaDescription || s?.metaDescription || post.metaDescription, slug: f.slug || (post.slugLocked ? post.slug : (s?.slug ?? post.slug)) };

  return (
    <div className="space-y-4" data-testid="post-editor">
      <div className="flex flex-wrap items-center gap-2">
        <Link href="/content/blog" className="inline-flex min-h-10 items-center gap-1 rounded-md px-2 text-sm text-muted-foreground hover:bg-muted">
          <ArrowRight className="size-4 ltr:rotate-180" aria-hidden="true" />
          {t("editor.back")}
        </Link>
        <span className={cn("rounded-full px-2.5 py-0.5 text-xs font-semibold", statusTone[post.status])} data-testid="post-status">
          {t(`blog.status.${post.status}` as TextKey)}
          {post.status === "scheduled" && post.publishAt && ` — ${formatDate(post.publishAt, { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" })}`}
        </span>
        {dirty && <span className="text-xs text-amber-700 dark:text-amber-400">{t("editor.unsaved")}</span>}
        <div className="ms-auto flex flex-wrap gap-2">
          {post.status === "published" && (
            <Button asChild variant="outline" className="h-11">
              <a href={`/blog/${post.slug}`} target="_blank" rel="noopener noreferrer" data-testid="post-view">
                <ExternalLink aria-hidden="true" />
                {t("editor.view")}
              </a>
            </Button>
          )}
          {!readOnly && (
            <Button className="h-11" onClick={() => void save()} disabled={busy || !dirty} data-testid="post-save">
              {busy ? <Loader2 className="animate-spin" aria-hidden="true" /> : <Check aria-hidden="true" />}
              {t("editor.save")}
            </Button>
          )}
          {post.status === "draft" && !post.canPublish && (
            <Button variant="outline" className="h-11" onClick={() => void act("submit", undefined, t("editor.submitted"))} disabled={busy} data-testid="post-submit">
              <Send className="rtl:-scale-x-100" aria-hidden="true" />
              {t("editor.submit")}
            </Button>
          )}
          {post.canPublish && post.status !== "published" && (
            <>
              <Button variant="outline" className="h-11" onClick={() => setSchedule(schedule ? null : { day: "", time: "10:00" })} data-testid="post-schedule">
                <CalendarClock aria-hidden="true" />
                {t("editor.schedule")}
              </Button>
              <Button className="h-11" onClick={() => void act("publish", { at: null, version: post.version }, t("editor.published"))} disabled={busy} data-testid="post-publish">
                {t("editor.publishNow")}
              </Button>
            </>
          )}
          {post.canPublish && (post.status === "published" || post.status === "scheduled") && (
            <Button variant="ghost" className="h-11" onClick={() => void act("unpublish", undefined, t("editor.unpublished"))} disabled={busy}>
              <Undo2 aria-hidden="true" />
              {t("editor.unpublish")}
            </Button>
          )}
        </div>
      </div>
      {schedule && (
        <form
          className="flex flex-wrap items-end gap-3 rounded-xl border bg-card p-4"
          onSubmit={(e) => {
            e.preventDefault();
            if (!schedule.day) return;
            const at = atTehran(schedule.day, schedule.time);
            void act("publish", { at, version: post.version }, t("editor.scheduled", { at: formatDate(at, { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" }) }));
          }}
        >
          <label className="space-y-1 text-sm">
            <span className="block font-medium">{t("editor.scheduleAt")}</span>
            <input type="date" dir="ltr" required value={schedule.day} onChange={(e) => setSchedule({ ...schedule, day: e.target.value })} className="h-11 rounded-md border bg-transparent px-2" data-testid="post-schedule-day" />
          </label>
          <input type="time" dir="ltr" value={schedule.time} onChange={(e) => setSchedule({ ...schedule, time: e.target.value })} aria-label={t("editor.scheduleAt")} className="h-11 rounded-md border bg-transparent px-2" />
          <Button type="submit" className="h-11" disabled={busy}>{t("editor.schedule")}</Button>
          <Button type="button" variant="ghost" size="icon" className="size-11" onClick={() => setSchedule(null)} aria-label={t("common.cancel")}>
            <X aria-hidden="true" />
          </Button>
        </form>
      )}
      <ErrorText error={error} />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-4">
          <label className="sr-only" htmlFor="post-title">{t("editor.titlePlaceholder")}</label>
          <textarea
            id="post-title"
            rows={2}
            value={f.title}
            readOnly={readOnly}
            onChange={(e) => {
              set({ title: e.target.value.replace(/\n/g, " ") });
              e.target.style.height = "auto";
              e.target.style.height = `${e.target.scrollHeight}px`;
            }}
            placeholder={t("editor.titlePlaceholder")}
            className="block w-full min-w-0 resize-none overflow-hidden bg-transparent text-3xl leading-[1.5] font-black outline-none sm:text-4xl"
            data-testid="post-title"
          />
          {editor && !readOnly && <EditorToolbar editor={editor} />}
          <div className="rounded-xl border bg-card p-4 sm:p-6">{editor ? <EditorContent editor={editor} /> : <div className="h-64 animate-pulse rounded bg-muted" />}</div>
        </div>

        <aside className="min-w-0 space-y-4" aria-label={t("seo.title")}>
          {editor && <ImageSettings editor={editor} />}

          <section className="space-y-3 rounded-xl border bg-card p-4" data-testid="seo-panel">
            <div className="flex items-center justify-between">
              <h2 className="font-bold">{t("seo.title")}</h2>
              <span className={cn("text-2xl font-black tabular-nums", scoreTone(s?.score ?? post.seoScore))} data-testid="seo-score">
                {formatNumber(s?.score ?? post.seoScore)}
                <span className="text-sm">٪</span>
              </span>
            </div>
            <div className="rounded-lg border bg-background p-3" aria-label={t("seo.preview")}>
              <p className="truncate text-xs text-muted-foreground" dir="ltr">…/blog/{effective.slug}</p>
              <p className="mt-1 line-clamp-2 text-base leading-7 font-semibold text-sky-800 dark:text-sky-300" data-testid="seo-google-title">{effective.seoTitle}</p>
              <p className="mt-1 line-clamp-3 text-sm leading-6 text-muted-foreground">{effective.meta}</p>
            </div>
            {s && <p className="text-xs text-muted-foreground">{t("seo.reading", { n: formatNumber(s.readingMinutes), w: formatNumber(s.wordCount) })}</p>}
            <ul className="space-y-1.5 text-sm" aria-label={t("seo.checklist")} data-testid="seo-checks">
              {(s?.checks ?? post.seoChecks).map((c) => (
                <li key={c.key} className="flex items-start gap-2">
                  <span aria-hidden="true" className={cn("mt-1.5 size-2.5 shrink-0 rounded-full", c.level === "good" ? "bg-emerald-600" : c.level === "warn" ? "bg-amber-500" : "bg-destructive")} />
                  <span>
                    {t(`seo.check.${c.key}` as TextKey)}
                    <span className="sr-only"> — {c.level}</span>
                  </span>
                </li>
              ))}
            </ul>
            {s && s.altFilled > 0 && <p className="rounded-md bg-amber-50 p-2 text-xs text-amber-900 dark:bg-amber-950 dark:text-amber-200">{t("seo.altFilled", { n: formatNumber(s.altFilled) })}</p>}
          </section>

          <section className="space-y-3 rounded-xl border bg-card p-4">
            <p className="text-xs text-muted-foreground">{t("seo.autoHelp")}</p>
            <SeoField label={t("seo.keyword")} value={f.focusKeyword} auto={s?.focusKeyword ?? post.focusKeyword} onChange={(v) => set({ focusKeyword: v })} readOnly={readOnly} testId="seo-keyword" />
            <SeoField label={t("seo.seoTitle")} value={f.seoTitle} auto={s?.seoTitle ?? post.seoTitle} onChange={(v) => set({ seoTitle: v })} max={60} readOnly={readOnly} />
            <SeoField label={t("seo.meta")} value={f.metaDescription} auto={s?.metaDescription ?? post.metaDescription} onChange={(v) => set({ metaDescription: v })} max={160} multiline readOnly={readOnly} />
            <SeoField label={t("seo.slug")} value={f.slug} auto={post.slugLocked ? "" : (s?.slug ?? post.slug)} onChange={(v) => set({ slug: v })} readOnly={readOnly} ltr />
          </section>

          <section className="space-y-3 rounded-xl border bg-card p-4">
            <label className="block space-y-1 text-sm">
              <span className="font-medium">{t("editor.category")}</span>
              <select value={f.categoryId} disabled={readOnly} onChange={(e) => set({ categoryId: e.target.value })} className="h-11 w-full rounded-md border bg-transparent px-2 text-base" data-testid="post-category">
                <option value="">{t("editor.noCategory")}</option>
                {(cats.data ?? []).map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>
            </label>
            <label className="block space-y-1 text-sm">
              <span className="font-medium">{t("editor.tags")}</span>
              <input value={f.tags} readOnly={readOnly} onChange={(e) => set({ tags: e.target.value })} className="h-11 w-full rounded-md border bg-transparent px-3 text-base" />
              <span className="block text-xs text-muted-foreground">{t("editor.tagsHelp")}</span>
            </label>
            <div className="space-y-2 text-sm">
              <span className="block font-medium">{t("editor.cover")}</span>
              {f.coverUrl ? (
                // eslint-disable-next-line @next/next/no-img-element -- uploaded at runtime
                <img src={f.coverUrl} alt={f.coverAlt} className="aspect-video w-full rounded-lg object-cover" />
              ) : (
                <p className="text-xs text-muted-foreground">{t("editor.coverHelp")}</p>
              )}
              {!readOnly && (
                <div className="flex gap-2">
                  <label className="inline-flex h-10 cursor-pointer items-center gap-2 rounded-md border px-3 hover:bg-muted">
                    {coverBusy ? <Loader2 className="size-4 animate-spin" aria-hidden="true" /> : <ImagePlus className="size-4" aria-hidden="true" />}
                    {coverBusy ? t("editor.uploading") : t("editor.upload")}
                    <input type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={(e) => void pickCover(e.target.files?.[0])} />
                  </label>
                  {f.coverImage && (
                    <Button type="button" variant="ghost" className="h-10" onClick={() => set({ coverImage: null, coverUrl: null })}>
                      {t("editor.remove")}
                    </Button>
                  )}
                </div>
              )}
              {f.coverImage && (
                <input value={f.coverAlt} readOnly={readOnly} onChange={(e) => set({ coverAlt: e.target.value })} placeholder={t("editor.coverAlt")} aria-label={t("editor.coverAlt")} className="h-11 w-full rounded-md border bg-transparent px-3 text-base" />
              )}
            </div>
          </section>

          <section className="space-y-2 rounded-xl border bg-card p-4" data-testid="seo-links">
            <h2 className="font-bold">{t("seo.links")}</h2>
            <p className="text-xs text-muted-foreground">{t("seo.linksHelp")}</p>
            {(s?.links ?? []).length === 0 && <p className="text-sm text-muted-foreground">{t("seo.noLinks")}</p>}
            <ul className="space-y-1">
              {(s?.links ?? []).map((l) => (
                <li key={l.url}>
                  <button type="button" disabled={readOnly} onClick={() => insertLink(l.url, l.title)} className="w-full rounded-md px-2 py-2 text-start text-sm hover:bg-muted">
                    <span className="block font-semibold">{l.title}</span>
                    <span className="block text-xs text-muted-foreground">{l.reason}</span>
                  </button>
                </li>
              ))}
            </ul>
          </section>

          {(s?.toc ?? post.toc).length > 0 && (
            <section className="space-y-2 rounded-xl border bg-card p-4">
              <h2 className="font-bold">{t("seo.toc")}</h2>
              <ol className="space-y-1 text-sm">
                {(s?.toc ?? post.toc).map((h) => (
                  <li key={h.id} className={h.level === 3 ? "ps-4 text-muted-foreground" : ""}>{h.text}</li>
                ))}
              </ol>
            </section>
          )}

          {post.canPublish || post.status === "draft" ? (
            <Button variant="ghost" className="h-11 w-full text-destructive" onClick={() => void archive()}>
              {t("editor.archive")}
            </Button>
          ) : null}
        </aside>
      </div>
    </div>
  );
}

function SeoField({ label, value, auto, onChange, max, multiline, readOnly, ltr, testId }: { label: string; value: string; auto: string; onChange: (v: string) => void; max?: number; multiline?: boolean; readOnly?: boolean; ltr?: boolean; testId?: string }) {
  const { t, formatNumber } = useI18n();
  const shown = value || auto;
  const common = {
    value,
    placeholder: auto,
    readOnly,
    dir: ltr ? "ltr" : undefined,
    onChange: (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => onChange(e.target.value),
    className: "w-full rounded-md border bg-transparent px-3 text-base placeholder:text-muted-foreground/80",
    "data-testid": testId,
  };
  return (
    <label className="block space-y-1 text-sm">
      <span className="flex items-center justify-between gap-2">
        <span className="font-medium">{label}</span>
        <span className={cn("rounded px-1.5 text-[11px] font-semibold", value ? "bg-foreground text-background" : "bg-muted text-muted-foreground")}>{value ? t("seo.manual") : t("seo.auto")}</span>
      </span>
      {multiline ? <textarea rows={3} {...common} className={cn(common.className, "py-2 leading-6")} /> : <input {...common} className={cn(common.className, "h-11")} />}
      {max && <span className={cn("block text-xs", shown.length > max ? "text-destructive" : "text-muted-foreground")}>{t("seo.chars", { n: formatNumber(shown.length), max: formatNumber(max) })}</span>}
    </label>
  );
}
