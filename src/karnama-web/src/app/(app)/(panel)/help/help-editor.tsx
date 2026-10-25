"use client";

import { Trash2 } from "lucide-react";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { useErrorMessage } from "@/components/app/error-text";
import { NumberField, SelectField, TextField, Toggle } from "@/components/app/form";
import { EditorContent, EditorToolbar, useRichEditor } from "@/components/blog/rich-editor";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faBlog } from "@/lib/i18n/fa-blog";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQueryClient } from "@/lib/query";
import type { HelpArticle } from "./page";

registerTexts(faBlog);

type Audience = HelpArticle["audience"];

/** Help articles use the blog's editor (PROMPT2 §12.3 / §13). */
export function HelpEditor({ article, onClose, onSaved }: { article: HelpArticle | null; onClose: () => void; onSaved: (slug: string) => void }) {
  const { t } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const [title, setTitle] = useState(article?.title ?? "");
  const [category, setCategory] = useState(article?.category ?? "");
  const [audience, setAudience] = useState<Audience>(article?.audience ?? "staff");
  const [pageKey, setPageKey] = useState(article?.pageKey ?? "");
  const [order, setOrder] = useState<number | null>(article?.sortOrder ?? 100);
  const [published, setPublished] = useState(article?.isPublished ?? true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [confirm, setConfirm] = useState(false);
  const editor = useRichEditor(article?.contentJson || '{"type":"doc","content":[]}', () => {}, t("help.f.bodyPlaceholder"));

  const save = async () => {
    if (!editor) return;
    setBusy(true);
    setError(null);
    try {
      const body = { title, slug: article?.slug ?? null, category, audience, pageKey: pageKey || null, contentJson: JSON.stringify(editor.getJSON()), sortOrder: order ?? 100, isPublished: published };
      const r = article
        ? await api<HelpArticle>(`/api/v1/help/${article.id}`, { method: "PUT", body })
        : await api<HelpArticle>("/api/v1/help", { method: "POST", body });
      notify.success(t(article ? "crud.saved" : "crud.created"));
      await qc.invalidateQueries({ queryKey: ["help"] });
      await qc.invalidateQueries({ queryKey: ["help-article"] });
      onSaved(r.slug);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    if (!article) return;
    try {
      await api(`/api/v1/help/${article.id}`, { method: "DELETE" });
      notify.success(t("help.deleted"));
      await qc.invalidateQueries({ queryKey: ["help"] });
      onClose();
      window.history.replaceState(null, "", "/help");
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  return (
    <EditorSheet title={article ? t("help.edit") : t("help.new")} onClose={onClose} onSubmit={save} busy={busy} error={error} wide testId="help-editor">
      <TextField label={t("help.f.title")} help={t("help.f.titleHelp")} value={title} onChange={setTitle} testId="help-title" />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField label={t("help.f.category")} help={t("help.f.categoryHelp")} value={category} onChange={setCategory} testId="help-category" />
        <SelectField label={t("help.f.audience")} help={t("help.f.audienceHelp")} value={audience} onChange={setAudience}
          options={(["everyone", "staff", "clients"] as Audience[]).map((a) => ({ value: a, label: t(`help.aud.${a}`) }))} />
        <TextField label={t("help.f.page")} help={t("help.f.pageHelp")} value={pageKey} onChange={setPageKey} optional dir="ltr" />
        <NumberField label={t("help.f.order")} help={t("help.f.orderHelp")} value={order} onChange={setOrder} min={0} max={10000} />
      </div>
      <Toggle label={t("help.f.published")} help={t("help.f.publishedHelp")} checked={published} onChange={setPublished} />
      <div className="space-y-2">
        <p className="text-sm font-medium">{t("help.f.body")}</p>
        {editor && <EditorToolbar editor={editor} />}
        <div className="min-h-64 rounded-xl border bg-card p-4" data-testid="help-body">{editor ? <EditorContent editor={editor} /> : <div className="h-64 animate-pulse rounded bg-muted" />}</div>
      </div>
      {article && (
        confirm ? (
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <span>{t("crud.confirmDelete", { name: article.title })}</span>
            <Button type="button" variant="destructive" className="h-11" onClick={remove}>{t("crud.confirmYes")}</Button>
            <Button type="button" variant="ghost" className="h-11" onClick={() => setConfirm(false)}>{t("common.cancel")}</Button>
          </div>
        ) : (
          <Button type="button" variant="ghost" className="h-11 text-destructive" onClick={() => setConfirm(true)}><Trash2 aria-hidden="true" />{t("common.delete")}</Button>
        )
      )}
    </EditorSheet>
  );
}
