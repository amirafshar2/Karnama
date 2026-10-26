"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { AlertTriangle, MessageSquare, Paperclip } from "lucide-react";
import dynamic from "next/dynamic";
import { useMemo, useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { api } from "@/lib/api";
import { faM3 } from "@/lib/i18n/fa-m3";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";
import { COLUMNS, type WorkCard, type WorkStatus } from "@/lib/work-types";
import { BoardScroller } from "./board-scroller";

registerTexts(faM3);

const WorkSheet = dynamic(() => import("./work-sheet").then((m) => m.WorkSheet), { ssr: false });

const BOARD = ["work-board"];

const DONE_SHOWN = 12;

export default function WorkBoardPage() {
  const allowed = useRequire([{ code: "work.view", scope: "assigned" }]);
  const { t, formatNumber, formatDate } = useI18n();
  const errorMessage = useErrorMessage();
  const qc = useQueryClient();
  const [mine, setMine] = useState(false);
  const [overdue, setOverdue] = useState(false);
  const [clientId, setClientId] = useState("");
  const [open, setOpen] = useState<string | null>(null);
  const [mobileCol, setMobileCol] = useState<WorkStatus>("todo");
  const [dragOver, setDragOver] = useState<WorkStatus | null>(null);
  // Finished work piles up; the column shows the latest few so the board stays short and easy to drop onto.
  const [allDone, setAllDone] = useState(false);

  const params = new URLSearchParams();
  if (mine) params.set("mine", "true");
  if (overdue) params.set("overdue", "true");
  if (clientId) params.set("clientId", clientId);
  const key = [...BOARD, params.toString()];
  const board = useQuery({ queryKey: key, enabled: allowed, queryFn: () => api<WorkCard[]>(`/api/v1/work/board?${params}`) });
  useLive(["work", "order"], [BOARD, ["work-item"]]);

  const byColumn = useMemo(() => {
    const m = new Map<WorkStatus, WorkCard[]>(COLUMNS.map((c) => [c, []]));
    for (const c of board.data ?? []) m.get(c.status)?.push(c);
    return m;
  }, [board.data]);
  const clients = useMemo(() => [...new Map((board.data ?? []).map((c) => [c.clientId, c.clientName])).entries()], [board.data]);

  if (!allowed) return null;

  /** Optimistic move: the card jumps at once; on refusal it goes back and the reason is shown. */
  const move = async (card: WorkCard, status: WorkStatus) => {
    if (card.status === status) return;
    const column = byColumn.get(status) ?? [];
    const rank = (column.at(-1)?.rank ?? 0) + 1000;
    const previous = qc.getQueryData<WorkCard[]>(key);
    qc.setQueryData<WorkCard[]>(key, (cards) => cards?.map((c) => (c.id === card.id ? { ...c, status, rank } : c)));
    try {
      const saved = await api<WorkCard>(`/api/v1/work/${card.id}/move`, { method: "POST", body: { status, rank, version: card.version } });
      qc.setQueryData<WorkCard[]>(key, (cards) => cards?.map((c) => (c.id === card.id ? saved : c)));
      void qc.invalidateQueries({ queryKey: BOARD });
    } catch (e) {
      qc.setQueryData(key, previous);
      notify.error(errorMessage(e));
    }
  };

  // Plain render functions (not inner components): a re-render while dragging must not remount the card being dragged.
  const renderCard = (c: WorkCard) => {
    const late = c.deadline && c.status !== "done" && new Date(c.deadline) < new Date();
    return (
      <li
        key={c.id}
        className="group rounded-lg border bg-card p-3 shadow-xs transition-colors hover:border-foreground/40"
        data-testid={`card-${c.id}`}
      >
        {/* The button itself is the drag handle (Chrome doesn't start a drag from a button inside a draggable parent). */}
        <button type="button" draggable onDragStart={(e) => e.dataTransfer.setData("text/plain", c.id)} className="block w-full cursor-grab text-start active:cursor-grabbing" onClick={() => setOpen(c.id)}>
          <div className="flex items-start justify-between gap-2">
            <span className="text-xs text-muted-foreground">
              #{formatNumber(c.orderNumber)} · <bdi>{c.clientName}</bdi>
            </span>
            {c.priority !== "normal" && (
              <span className={cn("rounded px-1.5 text-[11px] font-bold", c.priority === "urgent" ? "bg-destructive text-white" : "bg-accent text-accent-foreground")}>
                {t(`work.priority.${c.priority}` as TextKey)}
              </span>
            )}
          </div>
          <p className="mt-1 font-semibold leading-snug">{c.title}</p>
          <p className="mt-0.5 text-sm text-muted-foreground">
            {c.stageName} · {t("work.stage", { n: formatNumber(c.stageOrder), total: formatNumber(c.stageCount) })}
          </p>
          <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
            <span className={cn(!c.assigneeName && "italic")}>{c.assigneeName ?? t("work.unassigned")}</span>
            {c.deadline && (
              <span className={cn(late && "font-bold text-destructive")}>
                {late && <AlertTriangle className="me-0.5 inline size-3" aria-hidden="true" />}
                {late ? t("work.late") : formatDate(c.deadline, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}
              </span>
            )}
            {c.comments > 0 && (
              <span>
                <MessageSquare className="me-0.5 inline size-3" aria-hidden="true" />
                {formatNumber(c.comments)}
              </span>
            )}
            {c.files > 0 && (
              <span>
                <Paperclip className="me-0.5 inline size-3" aria-hidden="true" />
                {formatNumber(c.files)}
              </span>
            )}
            {c.revisionCount > 0 && <span className="text-destructive">{t("work.revisions", { count: formatNumber(c.revisionCount) })}</span>}
          </div>
        </button>
        {/* Phones and keyboards: no dragging — a plain "move to…" menu. */}
        <select
          aria-label={t("work.moveTo")}
          value=""
          onChange={(e) => e.target.value && move(c, e.target.value as WorkStatus)}
          className="mt-2 h-11 w-full rounded-md border bg-background px-2 text-sm md:hidden"
          data-testid={`move-${c.id}`}
        >
          <option value="">{t("work.moveTo")}</option>
          {COLUMNS.filter((s) => s !== c.status && s !== "waiting").map((s) => (
            <option key={s} value={s}>
              {t(`work.col.${s}` as TextKey)}
            </option>
          ))}
        </select>
      </li>
    );
  };

  const renderColumn = (status: WorkStatus) => {
    const cards = byColumn.get(status) ?? [];
    return (
      <section
        key={status}
        aria-labelledby={`col-${status}`}
        onDragOver={(e) => {
          e.preventDefault();
          setDragOver(status);
        }}
        onDragLeave={() => setDragOver(null)}
        onDrop={(e) => {
          e.preventDefault();
          setDragOver(null);
          const card = board.data?.find((c) => c.id === e.dataTransfer.getData("text/plain"));
          if (card) void move(card, status);
        }}
        className={cn(
          "flex max-h-full max-w-96 shrink-0 grow basis-64 snap-start flex-col self-start rounded-xl bg-muted/50 p-2 transition-colors",
          dragOver === status && "bg-accent/40 ring-2 ring-accent",
        )}
        data-col={status}
        data-testid={`col-${status}`}
      >
        <h2 id={`col-${status}`} className="flex items-center justify-between px-2 py-2 text-sm font-bold">
          {t(`work.col.${status}` as TextKey)}
          <span className="rounded-full bg-background px-2 text-xs font-medium">{formatNumber(cards.length)}</span>
        </h2>
        <ul className="kn-col flex min-h-0 flex-col gap-2 overflow-y-auto overscroll-contain">
          {(status === "done" && !allDone ? cards.slice(0, DONE_SHOWN) : cards).map(renderCard)}
          {cards.length === 0 && <li className="px-2 py-6 text-center text-xs text-muted-foreground">{t("work.empty")}</li>}
          {status === "done" && !allDone && cards.length > DONE_SHOWN && (
            <li>
              <button type="button" className="min-h-11 w-full rounded-lg border border-dashed text-sm hover:bg-background" onClick={() => setAllDone(true)} data-testid="work-done-all">
                {t("work.showAllDone", { count: formatNumber(cards.length) })}
              </button>
            </li>
          )}
        </ul>
      </section>
    );
  };

  return (
    <div className="space-y-4" data-wide-page>
      <header>
        <h1 className="text-2xl font-extrabold">{t("work.title")}</h1>
        <p className="mt-1 max-w-prose text-muted-foreground">{t("work.subtitle")}</p>
      </header>

      <div className="flex flex-wrap items-center gap-3">
        <label className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border bg-card px-3 text-sm font-medium">
          <input type="checkbox" className="size-5" checked={mine} onChange={(e) => setMine(e.target.checked)} data-testid="work-mine" />
          {t("work.mine")}
        </label>
        <label className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border bg-card px-3 text-sm font-medium">
          <input type="checkbox" className="size-5" checked={overdue} onChange={(e) => setOverdue(e.target.checked)} />
          {t("work.overdue")}
        </label>
        {clients.length > 1 && (
          <select value={clientId} onChange={(e) => setClientId(e.target.value)} className="h-11 rounded-md border bg-card px-3 text-sm" aria-label={t("orders.client")}>
            <option value="">{t("work.allClients")}</option>
            {clients.map(([id, name]) => (
              <option key={id} value={id}>
                {name}
              </option>
            ))}
          </select>
        )}
      </div>

      {board.isLoading && <div className="h-64 animate-pulse rounded-xl bg-muted" />}
      {board.data && board.data.length === 0 && !mine && !overdue && !clientId && (
        <p className="rounded-xl border border-dashed p-8 text-center text-muted-foreground">{t("work.emptyBoard")}</p>
      )}

      {/* Phone: one column at a time, chosen with chips. */}
      <div className="md:hidden">
        <div className="-mx-4 flex gap-2 overflow-x-auto px-4 pb-2" role="tablist">
          {COLUMNS.map((s) => (
            <button
              key={s}
              type="button"
              role="tab"
              aria-selected={mobileCol === s}
              onClick={() => setMobileCol(s)}
              className={cn("h-10 shrink-0 rounded-full border px-4 text-sm", mobileCol === s ? "border-foreground bg-foreground text-background" : "bg-card")}
              data-testid={`tab-${s}`}
            >
              {t(`work.col.${s}` as TextKey)} ({formatNumber(byColumn.get(s)?.length ?? 0)})
            </button>
          ))}
        </div>
        <ul className="mt-2 flex flex-col gap-2">
          {(byColumn.get(mobileCol) ?? []).map(renderCard)}
          {(byColumn.get(mobileCol) ?? []).length === 0 && <li className="py-8 text-center text-sm text-muted-foreground">{t("work.empty")}</li>}
        </ul>
      </div>

      {/* Tablet/desktop: all columns, drag and drop. */}
      <div className="hidden md:block">
        <BoardScroller columns={COLUMNS} counts={Object.fromEntries(COLUMNS.map((s) => [s, byColumn.get(s)?.length ?? 0]))}>
          {COLUMNS.map(renderColumn)}
        </BoardScroller>
      </div>

      {open && <WorkSheet id={open} onClose={() => setOpen(null)} />}
    </div>
  );
}
