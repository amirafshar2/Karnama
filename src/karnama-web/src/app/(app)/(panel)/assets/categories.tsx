"use client";

import { useState } from "react";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import { useQuery, useQueryClient } from "@/lib/query";
import type { Category } from "./types";

/** Equipment categories, editable in place (PROMPT2 §3: dynamic). */
export function Categories() {
  const { t, formatNumber } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const q = useQuery({ queryKey: ["asset-categories"], queryFn: () => api<Category[]>("/api/v1/asset-categories") });
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const save = async (id: string | null, body: { name: string; sortOrder: number; isActive: boolean }) => {
    setBusy(true);
    try {
      await api(id ? `/api/v1/asset-categories/${id}` : "/api/v1/asset-categories", { method: id ? "PUT" : "POST", body });
      notify.success(t("crud.saved"));
      if (!id) setName("");
      await qc.invalidateQueries({ queryKey: ["asset-categories"] });
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  if (q.error) return <ErrorText error={q.error} />;
  const list = q.data ?? [];
  return (
    <div className="max-w-xl space-y-4" data-testid="as-categories">
      <ul className="divide-y rounded-xl border bg-card">
        {list.map((c) => <Row key={c.id} c={c} busy={busy} onSave={(b) => save(c.id, b)} count={formatNumber(c.count)} />)}
      </ul>
      <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); if (name.trim()) void save(null, { name: name.trim(), sortOrder: (list.at(-1)?.sortOrder ?? 0) + 10, isActive: true }); }}>
        <label className="flex-1">
          <span className="sr-only">{t("as.categoryName")}</span>
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder={t("as.categoryName")} maxLength={40} className="h-11 w-full rounded-lg border bg-background px-3 text-base md:text-sm" data-testid="as-category-new" />
        </label>
        <Button type="submit" className="h-11" disabled={busy || !name.trim()} data-testid="as-category-add">{t("as.categoryAdd")}</Button>
      </form>
    </div>
  );
}

function Row({ c, busy, onSave, count }: { c: Category; busy: boolean; onSave: (b: { name: string; sortOrder: number; isActive: boolean }) => void; count: string }) {
  const { t } = useI18n();
  const [name, setName] = useState(c.name);
  return (
    <li className="flex flex-wrap items-center gap-2 p-3">
      <label className="min-w-40 flex-1">
        <span className="sr-only">{t("as.categoryName")}</span>
        <input value={name} onChange={(e) => setName(e.target.value)} maxLength={40} className="h-11 w-full rounded-lg border bg-background px-3 text-base md:text-sm" />
      </label>
      <span className="text-xs text-muted-foreground">{t("as.categoryCount", { n: count })}</span>
      <label className="flex min-h-11 items-center gap-2 text-sm">
        <input type="checkbox" className="size-5" checked={c.isActive} disabled={busy} onChange={(e) => onSave({ name, sortOrder: c.sortOrder, isActive: e.target.checked })} />
        {t("as.categoryActive")}
      </label>
      {name.trim() !== c.name && name.trim() && (
        <Button className="h-11" disabled={busy} onClick={() => onSave({ name: name.trim(), sortOrder: c.sortOrder, isActive: c.isActive })}>{t("common.save")}</Button>
      )}
    </li>
  );
}
