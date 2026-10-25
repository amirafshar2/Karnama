"use client";

import { ArrowRight, MessageCircleQuestion, Pencil, Plus, Route, Search } from "lucide-react";
import dynamic from "next/dynamic";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useState } from "react";
import { ErrorText } from "@/components/app/error-text";
import { startTour } from "@/components/help/tour";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { faCrud } from "@/lib/i18n/fa-crud";
import { faN12 } from "@/lib/i18n/fa-n12";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { useQuery } from "@/lib/query";
import { hasPermission } from "@/lib/types";
import { cn } from "@/lib/utils";

registerTexts(faN12);
registerTexts(faCrud);

const HelpEditor = dynamic(() => import("./help-editor").then((m) => m.HelpEditor), { ssr: false });

export type HelpItem = { id: string; title: string; slug: string; category: string; audience: "everyone" | "staff" | "clients"; pageKey: string | null; excerpt: string; sortOrder: number; isPublished: boolean };
export type HelpArticle = Omit<HelpItem, "excerpt"> & { contentHtml: string; contentJson: string; updatedAt: string; canEdit: boolean };

const GLOSSARY = ["package", "quota", "order", "stage", "timer", "useful", "review", "shift", "lead", "assetTag", "bot", "portal"] as const;

function Inner() {
  const { t } = useI18n();
  const { me } = useAuth();
  const router = useRouter();
  const path = usePathname();
  const slug = useSearchParams().get("a");
  const canEdit = hasPermission(me, "content.manage", "all");
  const isClient = me?.kind === "client";
  const [tab, setTab] = useState<"articles" | "glossary">("articles");
  const [editing, setEditing] = useState<HelpArticle | "new" | null>(null);
  const open = (s: string | null) => router.replace(s ? `${path}?a=${encodeURIComponent(s)}` : path, { scroll: false });

  return (
    <div className={cn("space-y-5", isClient && "text-lg")}>
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-extrabold">{t("help.title")}</h1>
          <p className="mt-1 max-w-prose text-muted-foreground">{t("help.subtitle")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" className="h-12 md:h-11" onClick={startTour} data-testid="help-tour"><Route aria-hidden="true" />{t("help.tour")}</Button>
          <Button variant={isClient ? "default" : "outline"} className="h-12 md:h-11" asChild>
            <Link href="/chat" title={t("help.askHelp")} data-testid="help-ask"><MessageCircleQuestion aria-hidden="true" />{t("help.ask")}</Link>
          </Button>
          {canEdit && <Button className="h-12 md:h-11" onClick={() => setEditing("new")} data-testid="help-new"><Plus aria-hidden="true" />{t("help.new")}</Button>}
        </div>
      </header>

      {slug ? (
        <ArticleView slug={slug} onBack={() => open(null)} onEdit={setEditing} />
      ) : (
        <>
          <div className="flex flex-wrap gap-2" role="tablist">
            {(["articles", "glossary"] as const).map((x) => (
              <button key={x} role="tab" aria-selected={tab === x} onClick={() => setTab(x)} data-testid={`help-tab-${x}`}
                className={cn("min-h-11 rounded-full border-2 px-4 text-sm font-semibold", tab === x ? "border-foreground bg-foreground text-background" : "border-border hover:border-foreground")}>
                {t(`help.tab.${x}`)}
              </button>
            ))}
          </div>
          {tab === "articles" ? <Articles onOpen={open} /> : <Glossary />}
        </>
      )}
      {editing && <HelpEditor article={editing === "new" ? null : editing} onClose={() => setEditing(null)} onSaved={(s) => { setEditing(null); open(s); }} />}
    </div>
  );
}

export default function HelpPage() {
  return (
    <Suspense>
      <Inner />
    </Suspense>
  );
}

function Articles({ onOpen }: { onOpen: (slug: string) => void }) {
  const { t } = useI18n();
  const [q, setQ] = useState("");
  const [cat, setCat] = useState<string | null>(null);
  const list = useQuery({ queryKey: ["help", q.trim()], queryFn: () => api<HelpItem[]>(`/api/v1/help?q=${encodeURIComponent(q.trim())}`) });
  const cats = [...new Set((list.data ?? []).map((a) => a.category))];
  const shown = (list.data ?? []).filter((a) => !cat || a.category === cat);
  return (
    <div className="space-y-4">
      <label className="relative block max-w-xl">
        <span className="sr-only">{t("help.search")}</span>
        <Search aria-hidden="true" className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("help.search")} className="h-12 w-full rounded-lg border bg-background ps-9 pe-3 text-base" data-testid="help-search" />
      </label>
      {cats.length > 1 && (
        <div className="flex flex-wrap gap-1.5">
          {[null, ...cats].map((c) => (
            <button key={c ?? "all"} type="button" onClick={() => setCat(c)} aria-pressed={cat === c}
              className={cn("min-h-10 rounded-md border px-3 text-sm", cat === c ? "border-foreground bg-muted font-semibold" : "hover:bg-muted")}>
              {c ?? t("help.allCategories")}
            </button>
          ))}
        </div>
      )}
      {list.error ? <ErrorText error={list.error} /> : !list.data ? <div className="h-40 animate-pulse rounded-xl bg-muted" /> : shown.length === 0 ? (
        <p className="rounded-xl border border-dashed p-6 text-center text-muted-foreground">{t("help.empty")}</p>
      ) : (
        <ul className="grid gap-3 md:grid-cols-2" data-testid="help-list">
          {shown.map((a) => (
            <li key={a.id}>
              <button type="button" onClick={() => onOpen(a.slug)} className="block h-full w-full rounded-xl border bg-card p-4 text-start hover:border-foreground" data-testid={`help-item-${a.slug}`}>
                <span className="block text-xs text-muted-foreground">{a.category}{!a.isPublished && ` · ${t("help.draft")}`}</span>
                <span className="mt-1 block font-bold">{a.title}</span>
                <span className="mt-1 block text-sm text-muted-foreground">{a.excerpt}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ArticleView({ slug, onBack, onEdit }: { slug: string; onBack: () => void; onEdit: (a: HelpArticle) => void }) {
  const { t, formatDate } = useI18n();
  const a = useQuery({ queryKey: ["help-article", slug], queryFn: () => api<HelpArticle>(`/api/v1/help/${encodeURIComponent(slug)}`) });
  return (
    <article className="space-y-4" data-testid="help-article">
      <div className="flex flex-wrap items-center gap-2">
        <Button variant="ghost" className="h-11" onClick={onBack}><ArrowRight aria-hidden="true" className="rtl:rotate-0 ltr:rotate-180" />{t("help.back")}</Button>
        {a.data?.canEdit && <Button variant="outline" className="h-11" onClick={() => onEdit(a.data!)} data-testid="help-edit"><Pencil aria-hidden="true" />{t("help.edit")}</Button>}
      </div>
      {a.error ? <ErrorText error={a.error} /> : !a.data ? <div className="h-64 animate-pulse rounded-xl bg-muted" /> : (
        <>
          <header>
            <p className="text-sm text-muted-foreground">{a.data.category}</p>
            <h2 className="text-2xl font-extrabold">{a.data.title}</h2>
            <p className="mt-1 text-xs text-muted-foreground">{t("help.updated", { when: formatDate(a.data.updatedAt, { day: "numeric", month: "long", year: "numeric" }) })}</p>
          </header>
          {/* Server-rendered from the editor JSON; only known nodes, every text encoded (TiptapRenderer). */}
          <div className="prose-fa" dangerouslySetInnerHTML={{ __html: a.data.contentHtml }} />
        </>
      )}
    </article>
  );
}

function Glossary() {
  const { t } = useI18n();
  return (
    <dl className="grid gap-3 md:grid-cols-2" data-testid="help-glossary">
      {GLOSSARY.map((k) => (
        <div key={k} className="rounded-xl border bg-card p-4">
          <dt className="font-bold">{t(`glossary.${k}` as TextKey)}</dt>
          <dd className="mt-1 text-muted-foreground">{t(`glossary.${k}Def` as TextKey)}</dd>
        </div>
      ))}
    </dl>
  );
}
