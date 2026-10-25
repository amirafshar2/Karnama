"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { AlarmClock, Phone, Plus, Search, Shuffle } from "lucide-react";
import dynamic from "next/dynamic";
import { useEffect, useMemo, useState } from "react";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { faCrud } from "@/lib/i18n/fa-crud";
import { faN7 } from "@/lib/i18n/fa-n7";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { hasPermission } from "@/lib/types";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";
import type { LeadItem, Marketer, Stage } from "./types";

registerTexts(faN7);
registerTexts(faCrud);

const LeadSheet = dynamic(() => import("./lead-sheet").then((m) => m.LeadSheet), { ssr: false });
const Report = dynamic(() => import("./report").then((m) => m.Report), { ssr: false });
const Settings = dynamic(() => import("./settings").then((m) => m.CrmSettingsPanel), { ssr: false });

type Tab = "board" | "today" | "report" | "settings";
/** A call started from the app: when the person comes back, ask "how did it go?" (PROMPT2 §4). */
const PENDING_CALL = "karnama.crm.pendingCall";

export default function CrmPage() {
  const allowed = useRequire([{ code: "crm.leads", scope: "own" }]);
  const { me } = useAuth();
  const { t } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const canAssign = hasPermission(me, "crm.assign", "all");
  const seesAll = hasPermission(me, "crm.leads", "all");
  const canPipeline = hasPermission(me, "crm.pipeline.manage", "all");
  const [tab, setTab] = useState<Tab>("board");
  const [owner, setOwner] = useState("");
  const [q, setQ] = useState("");
  const [open, setOpen] = useState<{ id: string | null; call?: number } | null>(null);
  const stages = useQuery({ queryKey: ["crm-stages"], enabled: allowed, queryFn: () => api<Stage[]>("/api/v1/crm/stages") });
  const marketers = useQuery({ queryKey: ["crm-marketers"], enabled: allowed && seesAll, queryFn: () => api<Marketer[]>("/api/v1/crm/marketers") });
  const board = useQuery({
    queryKey: ["crm-board", owner, q],
    enabled: allowed && tab === "board",
    queryFn: () => api<LeadItem[]>(`/api/v1/crm/board?${new URLSearchParams({ ...(owner ? { owner } : {}), ...(q.trim() ? { q: q.trim() } : {}) })}`),
  });
  const today = useQuery({ queryKey: ["crm-today"], enabled: allowed && tab === "today", queryFn: () => api<LeadItem[]>("/api/v1/crm/today") });
  useLive(["lead"], [["crm-board"], ["crm-today"]]);

  // Back from the phone app: open the "call result" form with a suggested duration.
  useEffect(() => {
    const check = () => {
      if (document.visibilityState !== "visible") return;
      let pending: { leadId: string; at: number } | null = null;
      try { pending = JSON.parse(sessionStorage.getItem(PENDING_CALL) ?? "null"); } catch { pending = null; }
      if (!pending) return;
      const seconds = Math.round((Date.now() - pending.at) / 1000);
      if (seconds < 3) return;
      try { sessionStorage.removeItem(PENDING_CALL); } catch { /* ignore */ }
      if (seconds < 4 * 3600) setOpen({ id: pending.leadId, call: Math.max(0, seconds - 5) });
    };
    document.addEventListener("visibilitychange", check);
    window.addEventListener("focus", check);
    return () => {
      document.removeEventListener("visibilitychange", check);
      window.removeEventListener("focus", check);
    };
  }, []);

  if (!allowed) return null;
  const refresh = () => {
    void qc.invalidateQueries({ queryKey: ["crm-board"] });
    void qc.invalidateQueries({ queryKey: ["crm-today"] });
  };

  const roundRobin = async () => {
    try {
      const r = await api<{ assigned: number }>("/api/v1/crm/round-robin", { method: "POST", body: { leadIds: [] } });
      notify.success(t("crm.roundRobinDone", { count: r.assigned }));
      refresh();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  const tabs: Tab[] = ["board", "today", "report", ...(canPipeline ? (["settings"] as Tab[]) : [])];
  return (
    <div className="space-y-5" data-testid="crm">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-extrabold">{t("crm.title")}</h1>
          <p className="mt-1 max-w-prose text-sm text-muted-foreground">{t("crm.subtitle")}</p>
        </div>
        <Button className="h-12 md:h-11" onClick={() => setOpen({ id: null })} data-testid="crm-new">
          <Plus aria-hidden="true" />
          {t("crm.new")}
        </Button>
      </header>

      <div className="flex flex-wrap gap-2" role="tablist">
        {tabs.map((x) => (
          <button key={x} role="tab" aria-selected={tab === x} onClick={() => setTab(x)} data-testid={`crm-tab-${x}`}
            className={cn("min-h-11 rounded-full border-2 px-4 text-sm font-semibold", tab === x ? "border-foreground bg-foreground text-background" : "border-border hover:border-foreground")}>
            {t(`crm.tab.${x}` as TextKey)}
          </button>
        ))}
      </div>

      {tab === "board" && (
        <>
          <div className="flex flex-wrap items-center gap-2">
            <label className="relative w-full sm:max-w-xs">
              <span className="sr-only">{t("crm.search")}</span>
              <Search className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
              <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("crm.search")} className="h-11 w-full rounded-lg border bg-background ps-9 pe-3 text-sm" data-testid="crm-search" />
            </label>
            {seesAll && (
              <select value={owner} onChange={(e) => setOwner(e.target.value)} aria-label={t("crm.owner")} className="h-11 rounded-lg border bg-background px-2 text-sm" data-testid="crm-owner-filter">
                <option value="">{t("crm.owner")}: {t("crm.owner.all")}</option>
                <option value="00000000-0000-0000-0000-000000000000">{t("crm.owner.none")}</option>
                {marketers.data?.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
              </select>
            )}
            {canAssign && (
              <Button variant="outline" className="h-11" onClick={roundRobin} data-testid="crm-round-robin">
                <Shuffle aria-hidden="true" />
                {t("crm.roundRobin")}
              </Button>
            )}
          </div>
          <ErrorText error={board.error ?? stages.error} />
          {stages.data && board.data && <Board stages={stages.data} leads={board.data} onOpen={(id) => setOpen({ id })} onChanged={refresh} />}
        </>
      )}

      {tab === "today" && (
        <section className="space-y-2">
          <ErrorText error={today.error} />
          {today.data?.length === 0 && <p className="rounded-xl border border-dashed p-8 text-center text-muted-foreground">{t("crm.todayEmpty")}</p>}
          <ul className="space-y-2" data-testid="crm-today">
            {today.data?.map((l) => <LeadCard key={l.id} lead={l} stages={stages.data ?? []} onOpen={() => setOpen({ id: l.id })} onChanged={refresh} />)}
          </ul>
        </section>
      )}

      {tab === "report" && <Report seesAll={seesAll} />}
      {tab === "settings" && canPipeline && <Settings />}

      {open && (
        <LeadSheet
          id={open.id}
          callSeconds={open.call}
          stages={stages.data ?? []}
          canAssign={canAssign}
          onClose={() => setOpen(null)}
          onChanged={refresh}
          onCalling={(leadId) => {
            try { sessionStorage.setItem(PENDING_CALL, JSON.stringify({ leadId, at: Date.now() })); } catch { /* ignore */ }
          }}
        />
      )}
    </div>
  );
}

/** Columns on wide screens; one column at a time on phones (pick it with the chips). */
function Board({ stages, leads, onOpen, onChanged }: { stages: Stage[]; leads: LeadItem[]; onOpen: (id: string) => void; onChanged: () => void }) {
  const { t, formatNumber } = useI18n();
  const active = stages.filter((s) => s.isActive);
  const [mobileStage, setMobileStage] = useState(active[0]?.id ?? "");
  const byStage = useMemo(() => Object.fromEntries(active.map((s) => [s.id, leads.filter((l) => l.stageId === s.id)])), [active, leads]);
  return (
    <>
      <div className="flex gap-2 overflow-x-auto pb-1 md:hidden" role="tablist">
        {active.map((s) => (
          <button key={s.id} role="tab" aria-selected={mobileStage === s.id} onClick={() => setMobileStage(s.id)}
            className={cn("min-h-10 shrink-0 rounded-full border px-3 text-sm", mobileStage === s.id ? "border-foreground bg-secondary font-bold" : "")}>
            {s.name} <span className="text-muted-foreground">({formatNumber(byStage[s.id]?.length ?? 0)})</span>
          </button>
        ))}
      </div>
      <div className="flex gap-3 overflow-x-auto pb-2" data-testid="crm-board">
        {active.map((s) => (
          <section key={s.id} className={cn("w-full shrink-0 space-y-2 rounded-xl bg-muted/50 p-2 md:w-72", mobileStage !== s.id && "hidden md:block")} data-testid={`crm-col-${s.name}`}>
            <h2 className="flex items-center justify-between px-1 text-sm font-bold">
              <span className={cn(s.kind === "won" && "text-emerald-700 dark:text-emerald-400", s.kind === "lost" && "text-muted-foreground")}>{s.name}</span>
              <span className="text-muted-foreground">{formatNumber(byStage[s.id]?.length ?? 0)}</span>
            </h2>
            <ul className="space-y-2">
              {(byStage[s.id] ?? []).map((l) => <LeadCard key={l.id} lead={l} stages={active} onOpen={() => onOpen(l.id)} onChanged={onChanged} />)}
              {(byStage[s.id] ?? []).length === 0 && <li className="p-3 text-center text-xs text-muted-foreground">{t("crm.columnEmpty")}</li>}
            </ul>
          </section>
        ))}
      </div>
    </>
  );
}

function LeadCard({ lead: l, stages, onOpen, onChanged }: { lead: LeadItem; stages: Stage[]; onOpen: () => void; onChanged: () => void }) {
  const { t, formatDate } = useI18n();
  const errorMessage = useErrorMessage();
  const due = l.nextFollowUpAt ? new Date(l.nextFollowUpAt) : null;
  const [now] = useState(() => Date.now());
  const overdue = due !== null && due.getTime() < now;
  const move = async (stageId: string) => {
    const stage = stages.find((s) => s.id === stageId);
    // "Lost" needs a reason: that happens in the lead sheet.
    if (stage?.kind === "lost") return onOpen();
    try {
      await api(`/api/v1/crm/leads/${l.id}/move`, { method: "POST", body: { stageId, lostReason: null } });
      notify.success(t("crm.moved"));
      onChanged();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  return (
    <li className="rounded-lg border bg-card p-3 shadow-sm" data-testid={`crm-lead-${l.name}`}>
      <button onClick={onOpen} className="block w-full text-start">
        <span className="block font-semibold">{l.name}</span>
        {l.business && <span className="block text-sm text-muted-foreground">{l.business}</span>}
        <span className="mt-1 flex flex-wrap gap-1 text-xs">
          {l.sourceName && <span className="rounded border px-1.5">{l.sourceName}</span>}
          {l.ownerName && <span className="rounded bg-secondary px-1.5 text-secondary-foreground">{l.ownerName}</span>}
          {l.clientId && <span className="rounded bg-emerald-100 px-1.5 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100">{t("crm.isClient")}</span>}
        </span>
        {due && (
          <span className={cn("mt-1 flex items-center gap-1 text-xs", overdue ? "font-bold text-destructive" : "text-muted-foreground")}>
            <AlarmClock className="size-3.5" aria-hidden="true" />
            {overdue ? `${t("crm.overdue")} — ` : ""}{t("crm.followUpAt", { time: formatDate(l.nextFollowUpAt!, { weekday: "short", hour: "2-digit", minute: "2-digit" }) })}
          </span>
        )}
      </button>
      <div className="mt-2 flex items-center gap-1">
        {l.phone && (
          <Button asChild variant="outline" size="icon" className="size-10 shrink-0">
            <a href={`tel:${l.phone}`} aria-label={`${t("crm.call")} ${l.name}`} onClick={() => { try { sessionStorage.setItem(PENDING_CALL, JSON.stringify({ leadId: l.id, at: Date.now() })); } catch { /* ignore */ } }}>
              <Phone aria-hidden="true" />
            </a>
          </Button>
        )}
        <select value="" onChange={(e) => e.target.value && void move(e.target.value)} aria-label={`${t("crm.moveTo")} ${l.name}`} className="h-10 min-w-0 flex-1 rounded-md border bg-background px-2 text-sm" data-testid="crm-move">
          <option value="">{t("crm.moveTo")}</option>
          {stages.filter((s) => s.id !== l.stageId).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
        </select>
      </div>
    </li>
  );
}
