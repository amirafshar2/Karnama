"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, Send } from "lucide-react";
import { useState } from "react";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { FilesPanel } from "@/components/app/files-panel";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { useIsMobile } from "@/hooks/use-mobile";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { cn } from "@/lib/utils";
import type { WorkDetail, WorkStatus } from "@/lib/work-types";

/** One card in full: brief, actions the person may take, assignee, files and the conversation. */
export function WorkSheet({ id, onClose }: { id: string; onClose: () => void }) {
  const { t, dir, formatNumber, formatDate } = useI18n();
  const isMobile = useIsMobile();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const { me } = useAuth();
  const detail = useQuery({ queryKey: ["work-item", id], queryFn: () => api<WorkDetail>(`/api/v1/work/${id}`) });
  const [busy, setBusy] = useState(false);
  const [changesNote, setChangesNote] = useState<string | null>(null);
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: ["work-item", id] });
    void qc.invalidateQueries({ queryKey: ["work-board"] });
  };

  const d = detail.data;
  const canWrite = !!d && (d.canAssign || d.card.assigneeId === me?.id);
  const move = async (status: WorkStatus, note?: string) => {
    if (!d) return;
    setBusy(true);
    try {
      await api(`/api/v1/work/${id}/move`, { method: "POST", body: { status, version: d.card.version, note } });
      notify.success(t("work.moved"));
      setChangesNote(null);
      refresh();
    } catch (e) {
      notify.error(errorMessage(e));
      refresh();
    } finally {
      setBusy(false);
    }
  };

  return (
    <Sheet open onOpenChange={(o) => !o && onClose()}>
      <SheetContent side={isMobile ? "bottom" : dir === "rtl" ? "left" : "right"} className={cn("flex flex-col gap-0 p-0", isMobile ? "h-[94dvh] rounded-t-2xl" : "w-full sm:max-w-2xl")}>
        <SheetHeader className="border-b p-5">
          <SheetTitle className="text-xl">{d?.card.title ?? t("common.loading")}</SheetTitle>
          {d && (
            <SheetDescription>
              {t("work.openOrder", { number: formatNumber(d.card.orderNumber) })} · <bdi>{d.card.clientName}</bdi> · {d.card.serviceName}
            </SheetDescription>
          )}
        </SheetHeader>
        {!d ? (
          <div className="grid flex-1 place-items-center">{detail.error ? <ErrorText error={detail.error} /> : <Loader2 className="animate-spin" aria-label={t("common.loading")} />}</div>
        ) : (
          <div className="flex-1 space-y-6 overflow-y-auto p-5" data-testid="work-sheet">
            {/* Stage progress */}
            <ol className="flex flex-wrap gap-2" aria-label={t("orders.stages")}>
              {d.order.stages.map((s) => (
                <li key={s.id} className={cn("rounded-full border px-3 py-1 text-xs", s.id === id ? "border-foreground bg-foreground text-background" : s.status === "done" ? "bg-muted line-through" : "")}>
                  {formatNumber(s.order)}. {s.name} — {t(`work.col.${s.status}` as TextKey)}
                </li>
              ))}
            </ol>

            <section className="space-y-3">
              <p className="text-sm">
                <span className="font-semibold">{t(`work.col.${d.card.status}` as TextKey)}</span>
                {d.card.deadline && <> · {t("work.dueIn", { when: formatDate(d.card.deadline, { weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" }) })}</>}
                {" · "}
                {t("work.estimate", { minutes: formatNumber(d.card.estimatedMinutes) })}
              </p>
              {changesNote !== null ? (
                <div className="space-y-2 rounded-lg border p-3">
                  <label htmlFor="changes-note" className="text-sm font-medium">
                    {t("work.changesNote")}
                  </label>
                  <textarea id="changes-note" rows={3} className="w-full rounded-md border bg-transparent p-2 text-base md:text-sm" value={changesNote} onChange={(e) => setChangesNote(e.target.value)} aria-describedby="changes-help" data-testid="changes-note" />
                  <p id="changes-help" className="text-xs text-muted-foreground">
                    {t("work.changesNoteHelp")}
                  </p>
                  <div className="flex gap-2">
                    <Button type="button" variant="destructive" className="h-11" disabled={busy || !changesNote.trim()} onClick={() => move("changesRequested", changesNote)} data-testid="changes-send">
                      {t("work.action.changesRequested")}
                    </Button>
                    <Button type="button" variant="ghost" className="h-11" onClick={() => setChangesNote(null)}>
                      {t("common.cancel")}
                    </Button>
                  </div>
                </div>
              ) : (
                <div className="flex flex-wrap gap-2">
                  {d.allowedMoves.map((s) => (
                    <Button
                      key={s}
                      type="button"
                      variant={s === "changesRequested" ? "outline" : s === "todo" ? "ghost" : "default"}
                      className="h-12 md:h-11"
                      disabled={busy}
                      onClick={() => (s === "changesRequested" ? setChangesNote("") : move(s))}
                      data-testid={`action-${s}`}
                    >
                      {t(`work.action.${s}` as TextKey)}
                    </Button>
                  ))}
                </div>
              )}
            </section>

            {d.canAssign && <AssignBox id={id} current={d.card.assigneeId} version={d.card.version} onDone={refresh} />}
            {!d.canAssign && (
              <p className="text-sm">
                {t("work.assignee")}: <strong>{d.card.assigneeName ?? t("work.unassigned")}</strong>
              </p>
            )}

            <section className="space-y-2">
              <h3 className="font-bold">{t("work.brief")}</h3>
              <dl className="grid gap-x-4 gap-y-2 rounded-lg border p-3 text-sm sm:grid-cols-[auto_1fr]">
                {d.order.answers.map((a) => (
                  <div key={a.key} className="contents">
                    <dt className="text-muted-foreground">{a.label}</dt>
                    <dd className="whitespace-pre-wrap">{Array.isArray(a.value) ? a.value.join("، ") : a.value === null || a.value === undefined ? "—" : String(a.value)}</dd>
                  </div>
                ))}
                {d.order.note && (
                  <div className="contents">
                    <dt className="text-muted-foreground">{t("orders.note")}</dt>
                    <dd className="whitespace-pre-wrap">{d.order.note}</dd>
                  </div>
                )}
              </dl>
              {d.order.files.filter((f) => f.workItemId !== id).length > 0 && (
                <FilesPanel files={d.order.files.filter((f) => f.workItemId !== id)} target={{ orderId: d.order.id, purpose: "brief" }} canUpload={false} canShare={false} onChanged={refresh} testId="order-files" />
              )}
            </section>

            <section className="space-y-2">
              <h3 className="font-bold">{t("work.files")}</h3>
              <FilesPanel files={d.files} target={{ orderId: d.order.id, workItemId: id, purpose: "deliverable" }} canUpload={canWrite} canShare={d.canTalkToClient} onChanged={refresh} />
            </section>

            <Comments id={id} detail={d} canWrite={canWrite} onSent={refresh} />
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
}

function AssignBox({ id, current, version, onDone }: { id: string; current: string | null; version: number; onDone: () => void }) {
  const { t, formatNumber } = useI18n();
  const errorMessage = useErrorMessage();
  const s = useQuery({ queryKey: ["work-suggest", id], queryFn: () => api<{ userId: string; name: string; openItems: number; openMinutes: number; hasStageRole: boolean }[]>(`/api/v1/work/${id}/suggestions`) });
  const [busy, setBusy] = useState(false);
  const assign = async (userId: string | null) => {
    setBusy(true);
    try {
      await api(`/api/v1/work/${id}/assign`, { method: "POST", body: { userId, version } });
      notify.success(t("work.assigned"));
      onDone();
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  const best = s.data?.[0];
  return (
    <section className="space-y-2" data-testid="assign">
      <h3 className="font-bold">{t("work.assignee")}</h3>
      <div className="flex flex-wrap items-center gap-2">
        <select className="h-12 min-w-0 flex-1 rounded-md border bg-card px-3 text-base md:h-11 md:text-sm" value={current ?? ""} disabled={busy} onChange={(e) => assign(e.target.value || null)} data-testid="assign-select" aria-label={t("work.assignee")}>
          <option value="">{t("work.unassigned")}</option>
          {(s.data ?? []).map((u) => (
            <option key={u.userId} value={u.userId}>
              {u.name} — {t("work.load", { count: formatNumber(u.openItems), hours: formatNumber(Math.round(u.openMinutes / 60)) })}
              {u.hasStageRole ? ` ✓ ${t("work.hasRole")}` : ""}
            </option>
          ))}
        </select>
        {best && best.userId !== current && (
          <Button type="button" className="h-12 md:h-11" disabled={busy} onClick={() => assign(best.userId)} data-testid="assign-suggested">
            {t("work.suggested")}: {best.name}
          </Button>
        )}
      </div>
    </section>
  );
}

function Comments({ id, detail, canWrite, onSent }: { id: string; detail: WorkDetail; canWrite: boolean; onSent: () => void }) {
  const { t, formatDate } = useI18n();
  const errorMessage = useErrorMessage();
  const [body, setBody] = useState("");
  const [toClient, setToClient] = useState(false);
  const [busy, setBusy] = useState(false);
  const send = async () => {
    if (!body.trim()) return;
    setBusy(true);
    try {
      await api(`/api/v1/work/${id}/comments`, { method: "POST", body: { body, visibility: toClient ? "client" : "internal" } });
      setBody("");
      onSent();
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="space-y-3" data-testid="comments">
      <h3 className="font-bold">{t("work.comments")}</h3>
      <ul className="space-y-2">
        {detail.comments.map((c) => (
          <li key={c.id} className={cn("rounded-lg border p-3 text-sm", c.visibility === "client" && "border-accent bg-accent/10", c.mine && "ms-6")}>
            <p className="mb-1 flex flex-wrap justify-between gap-2 text-xs text-muted-foreground">
              <span className="font-semibold text-foreground">{c.fromClient ? `${t("work.fromClient")}: ${c.authorName}` : c.authorName}</span>
              <span>
                {c.visibility === "client" ? t("work.toClient") : t("work.internal")} · {formatDate(c.createdAt, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}
              </span>
            </p>
            <p className="whitespace-pre-wrap">{c.body}</p>
          </li>
        ))}
        {detail.comments.length === 0 && <li className="text-sm text-muted-foreground">{t("work.noComments")}</li>}
      </ul>
      {canWrite && (
        <div className="space-y-2">
          <textarea rows={2} className="w-full rounded-md border bg-transparent p-2 text-base md:text-sm" placeholder={t("work.commentPlaceholder")} aria-label={t("work.commentPlaceholder")} value={body} onChange={(e) => setBody(e.target.value)} data-testid="comment-body" />
          <div className="flex flex-wrap items-center justify-between gap-2">
            {detail.canTalkToClient ? (
              <label className="flex min-h-11 items-center gap-2 text-sm">
                <input type="checkbox" className="size-5" checked={toClient} onChange={(e) => setToClient(e.target.checked)} />
                {t("work.toClient")}
              </label>
            ) : (
              <span className="text-xs text-muted-foreground">{t("work.internal")}</span>
            )}
            <Button type="button" className="h-11" disabled={busy || !body.trim()} onClick={send} data-testid="comment-send">
              <Send className="size-4" aria-hidden="true" />
              {t("work.send")}
            </Button>
          </div>
        </div>
      )}
    </section>
  );
}
