"use client";

import { Download, History, Loader2, Plus, Search } from "lucide-react";
import { Fragment, useEffect, useState } from "react";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api, refreshSession } from "@/lib/api";
import { API_BASE } from "@/lib/config";
import { faCrud } from "@/lib/i18n/fa-crud";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { tokenStore } from "@/lib/token-store";
import { useKeysetList } from "@/lib/use-keyset";
import { cn } from "@/lib/utils";

registerTexts(faCrud);

export type Column<T> = { header: string; cell: (item: T) => React.ReactNode; className?: string };

type Base = { id: string; isDeleted?: boolean; version?: number };

/**
 * The admin list used by every entity (docs/PROMPT.md §7): search, keyset "load more", show deleted,
 * Excel export, history, delete with in-page confirmation + 10-second undo, restore.
 * Desktop/tablet: table. Phone: one card per row.
 */
export function CrudList<T extends Base>({
  testId,
  title,
  subtitle,
  path,
  queryKey,
  params = {},
  newLabel,
  onNew,
  onEdit,
  columns,
  card,
  nameOf,
  emptyText,
  exportPath,
  historyType,
  canDelete = () => true,
  extraActions,
  filters,
  editLabel,
  deletable = true,
}: {
  testId: string;
  title: string;
  subtitle?: string;
  path: string;
  queryKey: string;
  params?: Record<string, string | undefined>;
  newLabel?: string;
  onNew?: () => void;
  onEdit?: (item: T) => void;
  columns: Column<T>[];
  card: (item: T) => React.ReactNode;
  nameOf: (item: T) => string;
  emptyText: string;
  exportPath?: string;
  historyType?: string;
  canDelete?: (item: T) => boolean;
  extraActions?: (item: T) => React.ReactNode;
  filters?: React.ReactNode;
  /** "Open" instead of "Edit" for records that are viewed more than edited (orders). */
  editLabel?: string;
  /** False for records that are never soft-deleted from a list (no "show deleted" switch). */
  deletable?: boolean;
}) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [search, setSearch] = useState("");
  const [q, setQ] = useState("");
  const [showDeleted, setShowDeleted] = useState(false);
  const [confirming, setConfirming] = useState<string | null>(null);
  const [history, setHistory] = useState<T | null>(null);
  const list = useKeysetList<T>(queryKey, path, { limit: "20", q: q || undefined, includeDeleted: showDeleted ? "true" : undefined, ...params });

  const onSearch = (v: string) => {
    setSearch(v);
    window.clearTimeout((onSearch as unknown as { tid?: number }).tid);
    (onSearch as unknown as { tid?: number }).tid = window.setTimeout(() => setQ(v.trim()), 300);
  };

  const remove = async (item: T) => {
    setConfirming(null);
    try {
      await api(`${path}/${item.id}`, { method: "DELETE" });
      await list.refresh();
      notify.message(t("crud.deletedToast", { name: nameOf(item) }), {
        duration: 10_000,
        action: {
          label: t("common.restore"),
          onClick: async () => {
            await api(`${path}/${item.id}/restore`, { method: "POST" });
            await list.refresh();
            notify.success(t("crud.restored"));
          },
        },
      });
    } catch (err) {
      notify.error(errorMessage(err));
    }
  };

  const restore = async (item: T) => {
    try {
      await api(`${path}/${item.id}/restore`, { method: "POST" });
      await list.refresh();
      notify.success(t("crud.restored"));
    } catch (err) {
      notify.error(errorMessage(err));
    }
  };

  const download = async () => {
    if (!exportPath) return;
    const get = () => fetch(API_BASE + exportPath, { credentials: "include", headers: tokenStore.access ? { Authorization: `Bearer ${tokenStore.access}` } : undefined });
    let res = await get();
    if (res.status === 401 && (await refreshSession())) res = await get();
    if (!res.ok) return notify.error(t("common.errorGeneric"));
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement("a");
    a.href = url;
    a.download = /filename="?([^";]+)/.exec(res.headers.get("content-disposition") ?? "")?.[1] ?? `${queryKey}.xlsx`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const Actions = ({ item }: { item: T }) =>
    confirming === item.id ? (
      <div className="flex flex-wrap items-center gap-2" role="group" aria-live="polite">
        <span className="text-sm">{t("crud.confirmDelete", { name: nameOf(item) })}</span>
        <Button size="sm" variant="destructive" className="h-12 md:h-10" onClick={() => remove(item)} data-testid={`${testId}-confirm-delete`}>
          {t("crud.confirmYes")}
        </Button>
        <Button size="sm" variant="ghost" className="h-12 md:h-10" onClick={() => setConfirming(null)}>
          {t("common.cancel")}
        </Button>
      </div>
    ) : (
      <div className="flex flex-wrap items-center gap-1 md:flex-nowrap">
        {item.isDeleted ? (
          <Button size="sm" variant="outline" className="h-12 md:h-10" onClick={() => restore(item)}>
            {t("common.restore")}
          </Button>
        ) : (
          <>
            {onEdit && (
              <Button size="sm" variant="ghost" className="h-12 md:h-10" onClick={() => onEdit(item)} data-testid={`${testId}-edit-${item.id}`}>
                {editLabel ?? t("common.edit")}
              </Button>
            )}
            {extraActions?.(item)}
            {canDelete(item) && (
              <Button size="sm" variant="ghost" className="h-12 text-destructive md:h-10" onClick={() => setConfirming(item.id)} data-testid={`${testId}-delete-${item.id}`}>
                {t("common.delete")}
              </Button>
            )}
          </>
        )}
        {historyType && (
          <Button size="sm" variant="ghost" className="h-12 md:h-10" onClick={() => setHistory(item)} aria-label={t("crud.history")}>
            <History aria-hidden="true" />
          </Button>
        )}
      </div>
    );

  return (
    <div className="space-y-6" data-testid={testId}>
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-extrabold">{title}</h1>
          {subtitle && <p className="mt-1 max-w-prose text-muted-foreground">{subtitle}</p>}
        </div>
        <div className="flex flex-wrap gap-2">
          {exportPath && (
            <Button variant="outline" className="h-12 md:h-11" onClick={download} title={t("crud.exportHelp")} data-testid={`${testId}-export`}>
              <Download aria-hidden="true" />
              {t("crud.export")}
            </Button>
          )}
          {onNew && (
            <Button className="h-12 md:h-11" onClick={onNew} data-testid={`${testId}-new`}>
              <Plus aria-hidden="true" />
              {newLabel}
            </Button>
          )}
        </div>
      </header>

      <div className="flex flex-wrap items-center gap-4">
        <div className="relative w-full sm:max-w-xs">
          <Search className="pointer-events-none absolute top-1/2 start-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
          <input
            type="search"
            className="h-12 w-full rounded-md border border-input bg-card ps-9 pe-3 text-base outline-none focus-visible:ring-2 focus-visible:ring-ring md:h-11 md:text-sm"
            placeholder={t("crud.search")}
            aria-label={t("common.search")}
            value={search}
            onChange={(e) => onSearch(e.target.value)}
            data-testid={`${testId}-search`}
          />
        </div>
        {filters}
        {deletable && (
          <label className="flex min-h-12 cursor-pointer items-center gap-2 text-sm font-medium">
            <input type="checkbox" checked={showDeleted} onChange={(e) => setShowDeleted(e.target.checked)} className="size-5 accent-[var(--foreground)]" data-testid={`${testId}-show-deleted`} />
            {t("crud.showDeleted")}
          </label>
        )}
      </div>

      {list.isLoading && (
        <div className="space-y-2" aria-busy="true">
          {Array.from({ length: 4 }, (_, i) => (
            <div key={i} className="h-16 animate-pulse rounded-md bg-muted" />
          ))}
        </div>
      )}
      <ErrorText error={list.error} />

      {!list.isLoading && list.items.length === 0 && !list.error && (
        <div className="rounded-xl border border-dashed p-8 text-center text-muted-foreground">
          <p>{q ? t("crud.noResults") : emptyText}</p>
          {!q && onNew && (
            <Button className="mt-4 h-12" onClick={onNew}>
              <Plus aria-hidden="true" />
              {newLabel}
            </Button>
          )}
        </div>
      )}

      {list.items.length > 0 && (
        <>
          <div className="hidden overflow-x-auto rounded-xl border bg-card md:block">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/40">
                <tr>
                  {columns.map((c, i) => (
                    <th key={i} scope="col" className={cn("h-11 px-4 text-start font-semibold", c.className)}>
                      {c.header}
                    </th>
                  ))}
                  <th scope="col" className="w-0">
                    <span className="sr-only">{t("crud.actions")}</span>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {list.items.map((item) => (
                  <Fragment key={item.id}>
                    <tr className={item.isDeleted ? "opacity-60" : undefined} data-testid={`${testId}-row-${item.id}`}>
                      {columns.map((c, i) => (
                        <td key={i} className={cn("px-4 py-3 align-middle", c.className)}>
                          {c.cell(item)}
                        </td>
                      ))}
                      <td className="px-4 py-3 align-middle whitespace-nowrap">{confirming !== item.id && <Actions item={item} />}</td>
                    </tr>
                    {confirming === item.id && (
                      <tr className="bg-destructive/5">
                        <td colSpan={columns.length + 1} className="px-4 py-3">
                          <Actions item={item} />
                        </td>
                      </tr>
                    )}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>

          <ul className="space-y-3 md:hidden">
            {list.items.map((item) => (
              <li key={item.id} className={cn("rounded-xl border bg-card p-4", item.isDeleted && "opacity-60")} data-testid={`${testId}-card-${item.id}`}>
                {card(item)}
                <div className="mt-3 border-t pt-2">
                  <Actions item={item} />
                </div>
              </li>
            ))}
          </ul>

          {list.hasMore && (
            <Button variant="outline" className="h-12 w-full md:h-11" onClick={() => void list.loadMore()} disabled={list.loadingMore}>
              {list.loadingMore && <Loader2 className="animate-spin" aria-hidden="true" />}
              {t("common.loadMore")}
            </Button>
          )}
        </>
      )}

      {history && historyType && <HistoryPanel type={historyType} id={history.id} name={nameOf(history)} onClose={() => setHistory(null)} />}
    </div>
  );
}

type HistoryEntry = { at: string; action: string; actorName: string | null; isDemo: boolean };

function HistoryPanel({ type, id, name, onClose }: { type: string; id: string; name: string; onClose: () => void }) {
  const { t, formatDate } = useI18n();
  const [rows, setRows] = useState<HistoryEntry[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    api<HistoryEntry[]>(`/api/v1/history/${type}/${id}`).then(setRows, setError);
  }, [type, id]);
  const label = (action: string) => {
    const verb = action.split(".").pop() ?? "";
    const known = ["created", "updated", "deleted", "restored", "assigned", "cancelled", "portalEnabled", "cover", "accepted", "rejected", "moved"];
    return known.includes(verb) ? t(`crud.verb.${verb}` as Parameters<typeof t>[0]) : action;
  };
  return (
    <div role="dialog" aria-modal="true" aria-labelledby="history-title" className="fixed inset-0 z-40 flex items-end justify-center bg-black/40 p-0 md:items-center md:p-4" onClick={onClose}>
      <div className="max-h-[85dvh] w-full max-w-lg overflow-y-auto rounded-t-2xl bg-card p-5 md:rounded-2xl" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between gap-2">
          <h2 id="history-title" className="text-lg font-bold">
            {t("crud.historyTitle")} — {name}
          </h2>
          <Button variant="ghost" className="h-11" onClick={onClose}>
            {t("common.close")}
          </Button>
        </div>
        <ErrorText error={error} />
        {rows?.length === 0 && <p className="mt-4 text-muted-foreground">{t("crud.historyEmpty")}</p>}
        <ol className="mt-4 space-y-3">
          {rows?.map((r, i) => (
            <li key={i} className="border-s-2 border-border ps-3 text-sm">
              <p className="font-semibold">{label(r.action)}</p>
              <p className="text-muted-foreground">
                {formatDate(r.at, { dateStyle: "medium", timeStyle: "short" })} · {r.actorName ? t("crud.historyBy", { name: r.actorName }) : t("crud.historySystem")}
              </p>
            </li>
          ))}
        </ol>
      </div>
    </div>
  );
}
