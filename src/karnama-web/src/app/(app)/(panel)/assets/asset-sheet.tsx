"use client";

import { AlertTriangle, Loader2, Pencil, Printer, Trash2 } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { JalaliDateField, SelectField, TextArea, TextField } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { useIsMobile } from "@/hooks/use-mobile";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQuery, useQueryClient } from "@/lib/query";
import { cn } from "@/lib/utils";
import { AssetEditor } from "./asset-editor";
import { AddPhoto, Thumb } from "./photos";
import { StatusChip } from "./status-chip";
import { STATUSES, type AssetDetail, type AssetStatus, type Assignment, type Person } from "./types";

type Mode = null | "handover" | "return" | "confirm" | "transfer" | "status" | "decline" | "acceptTransfer" | "delete";

/** One asset: who has it, the buttons that person (or a manager) may press, photos and the full history. */
export function AssetSheet({ id, tag, onClose, onOpened }: { id: string | null; tag: string | null; onClose: () => void; onOpened: (id: string) => void }) {
  const { t, dir, formatDate, formatNumber } = useI18n();
  const isMobile = useIsMobile();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const key = ["asset", id ?? `tag:${tag}`];
  const q = useQuery({
    queryKey: key,
    queryFn: async () => {
      const d = await api<AssetDetail>(id ? `/api/v1/assets/${id}` : `/api/v1/assets/tag/${encodeURIComponent(tag ?? "")}`);
      if (!id) onOpened(d.asset.id);
      return d;
    },
  });
  const [mode, setMode] = useState<Mode>(null);
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const d = q.data;
  const when = (s: string) => formatDate(s, { weekday: "short", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });

  const refresh = async (fresh?: AssetDetail) => {
    if (fresh) qc.setQueryData(key, fresh);
    await qc.invalidateQueries({ queryKey: ["assets"] });
    await qc.invalidateQueries({ queryKey: ["assets-mine"] });
    if (!fresh) await qc.invalidateQueries({ queryKey: key });
  };
  const run = async (path: string, body: unknown, done: string) => {
    setBusy(true);
    try {
      const fresh = await api<AssetDetail>(path, { method: "POST", body });
      notify.success(done);
      setMode(null);
      await refresh(fresh);
    } catch (e) {
      notify.error(errorMessage(e));
      await refresh();
    } finally {
      setBusy(false);
    }
  };

  return (
    <Sheet open onOpenChange={(o) => !o && onClose()}>
      <SheetContent side={isMobile ? "bottom" : dir === "rtl" ? "left" : "right"} className={cn("flex flex-col gap-0 p-0", isMobile ? "h-[94dvh] rounded-t-2xl" : "w-full sm:max-w-2xl")}>
        <SheetHeader className="border-b p-5">
          <SheetTitle className="text-xl">{d?.asset.name ?? (q.error ? t("as.notFound") : t("common.loading"))}</SheetTitle>
          {d && <SheetDescription dir="auto">{d.asset.categoryName} · {d.asset.tag}</SheetDescription>}
        </SheetHeader>
        {!d ? (
          <div className="grid flex-1 place-items-center p-5">{q.error ? <ErrorText error={q.error} /> : <Loader2 className="animate-spin" aria-label={t("common.loading")} />}</div>
        ) : (
          <div className="flex-1 space-y-6 overflow-y-auto p-5" data-testid="as-sheet">
            <div className="flex gap-2 overflow-x-auto pb-1">
              {d.photos.map((p) => <Thumb key={p.id} photo={p} />)}
              {d.can.manage && <AddPhoto assetId={d.asset.id} stage="profile" onAdded={() => refresh()} testId="as-photo-profile" />}
            </div>

            <section className="space-y-3 rounded-xl border p-4">
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <StatusChip a={d.asset} />
                {d.asset.holderName && <strong>{t("as.holder", { name: d.asset.holderName })}</strong>}
                {d.asset.overdue && (
                  <span className="flex items-center gap-1 font-semibold text-destructive"><AlertTriangle aria-hidden="true" className="size-4" />{t("as.overdue")}</span>
                )}
              </div>
              {d.current && <CurrentCard a={d.current} when={when} />}
              <Actions d={d} mode={mode} setMode={setMode} busy={busy} run={run} refresh={refresh} />
            </section>

            <section className="space-y-2">
              <h3 className="font-bold">{t("as.details")}</h3>
              <dl className="grid gap-x-4 gap-y-2 rounded-lg border p-3 text-sm sm:grid-cols-[auto_1fr]">
                {([
                  ["as.brand", d.asset.brand], ["as.model", d.asset.model], ["as.serial", d.asset.serial], ["as.tag", d.asset.tag],
                  ["as.purchasedOn", d.purchasedOn && formatDate(d.purchasedOn, { day: "numeric", month: "long", year: "numeric" })],
                  ["as.price", d.purchasePrice !== null ? formatNumber(d.purchasePrice) : null],
                  ["as.warranty", d.asset.warrantyUntil && formatDate(d.asset.warrantyUntil, { day: "numeric", month: "long", year: "numeric" })],
                  ["as.note", d.note],
                ] as [TextKey, string | null][]).filter(([, v]) => v).map(([k, v]) => (
                  <div key={k} className="contents">
                    <dt className="text-muted-foreground">{t(k)}</dt>
                    <dd className="whitespace-pre-wrap" dir="auto">{v}</dd>
                  </div>
                ))}
              </dl>
              {(d.can.manage || d.can.assign) && (
                <div className="flex flex-wrap gap-2 pt-1">
                  {d.can.manage && (
                    <Button variant="outline" className="h-11" onClick={() => setEditing(true)} data-testid="as-edit"><Pencil aria-hidden="true" />{t("as.edit")}</Button>
                  )}
                  <Button variant="outline" className="h-11" asChild>
                    <Link href={`/assets/label?id=${d.asset.id}`} data-testid="as-label"><Printer aria-hidden="true" />{t("as.label")}</Link>
                  </Button>
                  {!d.asset.holderId && <Button variant="outline" className="h-11" onClick={() => setMode("status")} data-testid="as-status">{t("as.setStatus")}</Button>}
                  {d.can.manage && !d.asset.holderId && (
                    <Button variant="ghost" className="h-11 text-destructive" onClick={() => setMode("delete")} data-testid="as-delete"><Trash2 aria-hidden="true" />{t("as.delete")}</Button>
                  )}
                </div>
              )}
              {mode === "status" && <StatusForm d={d} busy={busy} run={run} onCancel={() => setMode(null)} />}
              {mode === "delete" && (
                <div className="flex flex-wrap items-center gap-2 rounded-lg border border-destructive p-3 text-sm">
                  <span>{t("crud.confirmDelete", { name: d.asset.name })}</span>
                  <Button variant="destructive" className="h-11" disabled={busy} data-testid="as-delete-yes" onClick={async () => {
                    setBusy(true);
                    try {
                      await api(`/api/v1/assets/${d.asset.id}`, { method: "DELETE" });
                      notify.success(t("as.deleted"));
                      await refresh();
                      onClose();
                    } catch (e) {
                      notify.error(errorMessage(e));
                    } finally {
                      setBusy(false);
                    }
                  }}>{t("crud.confirmYes")}</Button>
                  <Button variant="ghost" className="h-11" onClick={() => setMode(null)}>{t("common.cancel")}</Button>
                </div>
              )}
            </section>

            {d.history.length > 0 && (
              <section className="space-y-2">
                <h3 className="font-bold">{t("as.history")}</h3>
                <ol className="space-y-2 border-s-2 ps-4" data-testid="as-history">
                  {d.history.map((e) => (
                    <li key={e.id} className="text-sm">
                      <span className="font-medium">{t(`as.ev.${e.kind}` as TextKey, { user: e.userName ?? "—" })}</span>
                      {e.actorName && <span className="text-muted-foreground"> · {t("as.by", { name: e.actorName })}</span>}
                      <span className="block text-xs text-muted-foreground">{when(e.at)}</span>
                      {e.note && <span className="block text-xs whitespace-pre-wrap" dir="auto">{e.note}</span>}
                    </li>
                  ))}
                </ol>
              </section>
            )}

            {d.past.length > 0 && (
              <section className="space-y-2">
                <h3 className="font-bold">{t("as.pastHandovers")}</h3>
                <ul className="space-y-2">
                  {d.past.map((a) => (
                    <li key={a.id} className="rounded-lg border p-3 text-sm">
                      <div className="font-semibold">{a.userName} — {t(`as.state.${a.state}` as TextKey)}</div>
                      <div className="text-xs text-muted-foreground">{t("as.handedAt", { when: when(a.handedAt) })}{a.returnedAt && ` · ${t("as.returnedAt", { when: when(a.returnedAt) })}`}</div>
                      {a.damage && <div className="mt-1 text-xs text-destructive">{t("as.damage")}: {a.damage}</div>}
                    </li>
                  ))}
                </ul>
              </section>
            )}
          </div>
        )}
        {editing && d && <AssetEditor asset={d} onClose={() => setEditing(false)} onSaved={() => { setEditing(false); void refresh(); }} />}
      </SheetContent>
    </Sheet>
  );
}

function CurrentCard({ a, when }: { a: Assignment; when: (s: string) => string }) {
  const { t, formatDate } = useI18n();
  return (
    <div className="space-y-1 rounded-lg bg-muted/50 p-3 text-sm" data-testid="as-current">
      <div className="font-semibold">{a.userName} — {t(`as.state.${a.state}` as TextKey)}</div>
      <div className="text-xs text-muted-foreground">
        {t("as.handedAt", { when: when(a.handedAt) })} · {t("as.handedBy", { name: a.handedByName })}
        {a.acceptedAt && <> · {t("as.acceptedAt", { when: when(a.acceptedAt) })}</>}
      </div>
      {a.expectedReturn && <div className="text-xs">{t("as.returnBy", { date: formatDate(a.expectedReturn, { weekday: "long", day: "numeric", month: "long" }) })}</div>}
      {a.conditionOut && <div className="text-xs">{t("as.conditionOut")}: {a.conditionOut}</div>}
      {a.accessories.length > 0 && <div className="text-xs">{t("as.accessories")}: {a.accessories.join("، ")}</div>}
      {a.conditionIn && <div className="text-xs">{t("as.conditionIn")}: {a.conditionIn}</div>}
      {a.damage && <div className="text-xs font-semibold text-destructive">{t("as.damage")}: {a.damage}</div>}
      {a.transferToName && <div className="text-xs font-semibold">{t("as.transferWaiting", { name: a.transferToName })}</div>}
    </div>
  );
}

type RunFn = (path: string, body: unknown, done: string) => Promise<void>;

function Actions({ d, mode, setMode, busy, run, refresh }: { d: AssetDetail; mode: Mode; setMode: (m: Mode) => void; busy: boolean; run: RunFn; refresh: () => Promise<void> }) {
  const { t } = useI18n();
  const c = d.current;
  const base = c ? `/api/v1/assets/assignments/${c.id}` : "";
  const [note, setNote] = useState("");
  const [damage, setDamage] = useState("");
  const [next, setNext] = useState<AssetStatus>("available");
  const [to, setTo] = useState("");

  if (mode === "handover") return <HandOverForm d={d} busy={busy} run={run} onCancel={() => setMode(null)} />;
  if (mode === "transfer" && c)
    return (
      <div className="space-y-3 rounded-lg border p-3">
        <p className="text-sm text-muted-foreground">{t("as.transferHelp")}</p>
        <PeoplePicker value={to} onChange={setTo} exclude={c.userId} />
        <div className="flex gap-2">
          <Button className="h-11" disabled={busy || !to} data-testid="as-transfer-send" onClick={() => run(`${base}/transfer`, { userId: to }, t("as.transferSent"))}>{t("as.transfer")}</Button>
          <Button variant="ghost" className="h-11" onClick={() => setMode(null)}>{t("common.cancel")}</Button>
        </div>
      </div>
    );
  if ((mode === "return" || mode === "confirm" || mode === "acceptTransfer" || mode === "decline") && c)
    return (
      <div className="space-y-3 rounded-lg border p-3">
        {mode === "confirm" && (
          <SelectField label={t("as.confirmReturnHelp")} value={next} onChange={setNext} testId="as-return-status"
            options={(["available", "inRepair", "lost"] as AssetStatus[]).map((s) => ({ value: s, label: t(`as.status.${s}` as TextKey) }))} />
        )}
        <TextField label={mode === "decline" ? t("as.note") : t("as.conditionIn")} value={note} onChange={setNote} optional testId="as-return-condition" />
        {(mode === "return" || mode === "confirm") && <TextArea label={t("as.damage")} help={t("as.damageHelp")} value={damage} onChange={setDamage} optional testId="as-return-damage" />}
        {(mode === "return" || mode === "confirm") && (
          <div className="flex gap-2 overflow-x-auto">
            {d.photos.filter((p) => p.stage === "return" && p.assignmentId === c.id).map((p) => <Thumb key={p.id} photo={p} />)}
            <AddPhoto assetId={d.asset.id} stage="return" assignmentId={c.id} onAdded={refresh} testId="as-photo-return" />
          </div>
        )}
        <div className="flex gap-2">
          <Button className="h-11" disabled={busy} data-testid="as-mode-submit" onClick={() =>
            mode === "return" ? run(`${base}/return`, { condition: note || null, damage: damage || null }, t("as.returnSubmitted"))
            : mode === "confirm" ? run(`${base}/return/confirm`, { status: next, condition: note || null, damage: damage || null }, t("as.returned"))
            : mode === "decline" ? run(`${base}/decline`, { note: note || null }, t("as.declined"))
            : run(`${base}/transfer/accept`, { condition: note || null }, t("as.transferred"))}>
            {t(mode === "return" ? "as.return" : mode === "confirm" ? "as.confirmReturn" : mode === "decline" ? "as.decline" : "as.acceptTransfer")}
          </Button>
          <Button variant="ghost" className="h-11" onClick={() => setMode(null)}>{t("common.cancel")}</Button>
        </div>
      </div>
    );

  const buttons: React.ReactNode[] = [];
  if (d.can.accept && c) {
    buttons.push(
      <div key="accept" className="w-full space-y-2">
        <p className="text-sm text-muted-foreground">{t("as.acceptHelp")}</p>
        <div className="flex gap-2 overflow-x-auto">
          {d.photos.filter((p) => p.stage === "handOver" && p.assignmentId === c.id).map((p) => <Thumb key={p.id} photo={p} />)}
        </div>
        <div className="flex flex-wrap gap-2">
          <Button className="h-12 md:h-11" disabled={busy} onClick={() => run(`${base}/accept`, {}, t("as.accepted"))} data-testid="as-accept">{t("as.accept")}</Button>
          <Button variant="outline" className="h-12 md:h-11" disabled={busy} onClick={() => setMode("decline")} data-testid="as-decline">{t("as.decline")}</Button>
        </div>
      </div>,
    );
  }
  if (d.can.acceptTransfer) buttons.push(<Button key="at" className="h-12 md:h-11" disabled={busy} onClick={() => setMode("acceptTransfer")} data-testid="as-accept-transfer">{t("as.acceptTransfer")}</Button>);
  if (d.can.submitReturn) buttons.push(<Button key="ret" variant="outline" className="h-12 md:h-11" disabled={busy} onClick={() => setMode("return")} data-testid="as-return">{t("as.return")}</Button>);
  if (d.can.transfer && c && !c.transferToId) buttons.push(<Button key="tr" variant="outline" className="h-12 md:h-11" disabled={busy} onClick={() => setMode("transfer")} data-testid="as-transfer">{t("as.transfer")}</Button>);
  if (c?.transferToId && (d.can.transfer || d.can.acceptTransfer))
    buttons.push(<Button key="tc" variant="ghost" className="h-12 md:h-11" disabled={busy} onClick={() => run(`${base}/transfer/cancel`, {}, t("crud.saved"))} data-testid="as-transfer-cancel">{t("as.transferCancel")}</Button>);
  if (d.can.assign) {
    if (!c && d.asset.status === "available") buttons.push(<Button key="ho" className="h-12 md:h-11" onClick={() => setMode("handover")} data-testid="as-handover">{t("as.handover")}</Button>);
    if (c && (c.state === "active" || c.state === "returnSubmitted"))
      buttons.push(<Button key="cr" className="h-12 md:h-11" disabled={busy} onClick={() => setMode("confirm")} data-testid="as-confirm-return">{t("as.confirmReturn")}</Button>);
    if (c?.state === "pendingAcceptance") {
      buttons.push(
        <div key="hop" className="flex w-full gap-2 overflow-x-auto">
          {d.photos.filter((p) => p.stage === "handOver" && p.assignmentId === c.id).map((p) => <Thumb key={p.id} photo={p} />)}
          <AddPhoto assetId={d.asset.id} stage="handOver" assignmentId={c.id} onAdded={refresh} testId="as-photo-handover" />
        </div>,
      );
      buttons.push(<Button key="cn" variant="ghost" className="h-12 md:h-11" disabled={busy} onClick={() => run(`${base}/cancel`, {}, t("as.cancelled"))} data-testid="as-cancel">{t("as.cancel")}</Button>);
    }
  }
  return buttons.length ? <div className="flex flex-wrap gap-2">{buttons}</div> : null;
}

function PeoplePicker({ value, onChange, exclude }: { value: string; onChange: (v: string) => void; exclude?: string }) {
  const { t } = useI18n();
  const people = useQuery({ queryKey: ["asset-people"], queryFn: () => api<Person[]>("/api/v1/assets/people") });
  return (
    <SelectField label={t("as.handoverTo")} value={value} onChange={onChange} testId="as-person"
      options={(people.data ?? []).filter((p) => p.id !== exclude).map((p) => ({ value: p.id, label: p.name }))} />
  );
}

function HandOverForm({ d, busy, run, onCancel }: { d: AssetDetail; busy: boolean; run: RunFn; onCancel: () => void }) {
  const { t } = useI18n();
  const [to, setTo] = useState("");
  const [ret, setRet] = useState<string | null>(null);
  const [condition, setCondition] = useState("");
  const [acc, setAcc] = useState("");
  return (
    <div className="space-y-3 rounded-lg border p-3" data-testid="as-handover-form">
      <PeoplePicker value={to} onChange={setTo} />
      <JalaliDateField label={t("as.expectedReturn")} help={t("as.expectedReturnHelp")} value={ret} onChange={setRet} optional testId="as-return-date" />
      <TextField label={t("as.condition")} help={t("as.conditionHelp")} value={condition} onChange={setCondition} optional testId="as-condition" />
      <TextField label={t("as.accessories")} help={t("as.accessoriesHelp")} value={acc} onChange={setAcc} optional testId="as-accessories" />
      <div className="flex gap-2">
        <Button className="h-11" disabled={busy || !to} data-testid="as-handover-save"
          onClick={() => run(`/api/v1/assets/${d.asset.id}/handover`, {
            userId: to, expectedReturn: ret ? ret.slice(0, 10) : null, condition: condition || null,
            accessories: acc.split(/[،,]/).map((x) => x.trim()).filter(Boolean), version: d.asset.version,
          }, t("as.handedOver"))}>
          {t("as.handover")}
        </Button>
        <Button variant="ghost" className="h-11" onClick={onCancel}>{t("common.cancel")}</Button>
      </div>
    </div>
  );
}

function StatusForm({ d, busy, run, onCancel }: { d: AssetDetail; busy: boolean; run: RunFn; onCancel: () => void }) {
  const { t } = useI18n();
  const [s, setS] = useState<AssetStatus>(d.asset.status === "available" ? "inRepair" : "available");
  const [note, setNote] = useState("");
  return (
    <div className="space-y-3 rounded-lg border p-3">
      <SelectField label={t("as.setStatus")} value={s} onChange={setS} testId="as-status-select"
        options={STATUSES.filter((x) => x !== "assigned").map((x) => ({ value: x, label: t(`as.status.${x}` as TextKey) }))} />
      <TextField label={t("as.note")} value={note} onChange={setNote} optional />
      <div className="flex gap-2">
        <Button className="h-11" disabled={busy} data-testid="as-status-save" onClick={() => run(`/api/v1/assets/${d.asset.id}/status`, { status: s, note: note || null }, t("as.statusChanged"))}>{t("common.save")}</Button>
        <Button variant="ghost" className="h-11" onClick={onCancel}>{t("common.cancel")}</Button>
      </div>
    </div>
  );
}
