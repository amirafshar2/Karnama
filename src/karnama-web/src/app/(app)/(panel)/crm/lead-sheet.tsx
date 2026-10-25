"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { ArrowLeftRight, Briefcase, CalendarClock, MessageSquare, Phone, PhoneCall, UserCheck } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { useErrorMessage } from "@/components/app/error-text";
import { JalaliDateField, NumberField, SelectField, TextArea, TextField } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { cn } from "@/lib/utils";
import { tehran, type Activity, type LeadDetail, type Marketer, type Source, type Stage } from "./types";

type Props = {
  id: string | null;
  callSeconds?: number;
  stages: Stage[];
  canAssign: boolean;
  onClose: () => void;
  onChanged: () => void;
  onCalling: (leadId: string) => void;
};

/** One lead: details, quick actions (call, follow-up, stage, hand-over, client) and its history. */
export function LeadSheet(props: Props) {
  const { t } = useI18n();
  const detail = useQuery({ queryKey: ["crm-lead", props.id], enabled: props.id !== null, gcTime: 0, queryFn: () => api<LeadDetail>(`/api/v1/crm/leads/${props.id}`) });
  if (props.id && !detail.data)
    return <EditorSheet title={t("crm.form.edit")} onClose={props.onClose} onSubmit={() => {}} busy={false} error={detail.error} loading={!detail.error} wide>{null}</EditorSheet>;
  return <Sheet {...props} initial={detail.data ?? null} />;
}

function Sheet({ callSeconds, stages, canAssign, onClose, onChanged, onCalling, initial }: Props & { initial: LeadDetail | null }) {
  const { t } = useI18n();
  const qc = useQueryClient();
  const sources = useQuery({ queryKey: ["crm-sources"], queryFn: () => api<Source[]>("/api/v1/crm/sources") });
  const marketers = useQuery({ queryKey: ["crm-marketers"], enabled: canAssign, queryFn: () => api<Marketer[]>("/api/v1/crm/marketers") });
  const [d, setD] = useState<LeadDetail | null>(initial);
  const l = d?.lead;
  const [form, setForm] = useState({
    name: l?.name ?? "", phone: l?.phone ?? "", business: l?.business ?? "", industry: d?.industry ?? "", city: l?.city ?? "شیراز", instagram: l?.instagram ?? "",
    sourceId: l?.sourceId ?? "", tags: (l?.tags ?? []).join("، "), budget: l?.budget != null ? Math.round(l.budget / 10) : null as number | null, note: d?.note ?? "",
    ownerId: l?.ownerId ?? "",
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const set = (patch: Partial<typeof form>) => setForm((f) => ({ ...f, ...patch }));

  const refreshed = (x: LeadDetail) => {
    setD(x);
    qc.setQueryData(["crm-lead", x.lead.id], x);
    onChanged();
  };

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const body = {
        name: form.name, phone: form.phone || null, business: form.business || null, industry: form.industry || null, city: form.city || null,
        instagram: form.instagram || null, sourceId: form.sourceId || null, tags: form.tags.split(/[,،\s]+/).filter(Boolean), budget: form.budget != null ? form.budget * 10 : null,
        note: form.note || null, stageId: null, ownerId: form.ownerId || null, nextFollowUpAt: l?.nextFollowUpAt ?? null, version: l?.version ?? null,
      };
      const r = await api<LeadDetail>(l ? `/api/v1/crm/leads/${l.id}` : "/api/v1/crm/leads", { method: l ? "PUT" : "POST", body });
      notify.success(t(l ? "crud.saved" : "crud.created"));
      refreshed(r);
      if (!l) onClose();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  return (
    <EditorSheet title={l ? `${l.name}${l.business ? ` — ${l.business}` : ""}` : t("crm.form.create")} onClose={onClose} onSubmit={submit} busy={busy} error={error} wide testId="crm-lead-sheet">
      {l && d && (
        <Actions d={d} stages={stages} canAssign={canAssign} marketers={marketers.data ?? []} callSeconds={callSeconds} onCalling={onCalling} onDone={refreshed} />
      )}
      {d?.message && (
        <blockquote className="rounded-lg border-s-4 border-primary bg-muted/50 p-3 text-sm">
          <p className="mb-1 text-xs font-bold text-muted-foreground">{t("crm.message")}{d.origin ? ` — ${d.origin}` : ""}</p>
          <p className="whitespace-pre-wrap">{d.message}</p>
        </blockquote>
      )}
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField label={t("crm.name")} value={form.name} onChange={(v) => set({ name: v })} testId="crm-name" />
        <TextField label={t("crm.phone")} help={t("crm.phoneHelp")} value={form.phone} onChange={(v) => set({ phone: v })} dir="ltr" inputMode="tel" testId="crm-phone" />
        <TextField label={t("crm.business")} value={form.business} onChange={(v) => set({ business: v })} optional testId="crm-business" />
        <TextField label={t("crm.industry")} value={form.industry} onChange={(v) => set({ industry: v })} optional />
        <TextField label={t("crm.city")} value={form.city} onChange={(v) => set({ city: v })} optional />
        <TextField label={t("crm.instagram")} value={form.instagram} onChange={(v) => set({ instagram: v })} dir="ltr" optional />
        <SelectField label={t("crm.source")} value={form.sourceId} onChange={(v) => set({ sourceId: v })} optional options={(sources.data ?? []).filter((s) => s.isActive || s.id === form.sourceId).map((s) => ({ value: s.id, label: s.name }))} testId="crm-source" />
        <NumberField label={t("crm.budget")} value={form.budget} onChange={(v) => set({ budget: v })} optional min={0} />
        <TextField label={t("crm.tags")} help={t("crm.tagsHelp")} value={form.tags} onChange={(v) => set({ tags: v })} optional />
        {canAssign && !l && (
          <SelectField label={t("crm.owner")} value={form.ownerId} onChange={(v) => set({ ownerId: v })} optional options={(marketers.data ?? []).map((m) => ({ value: m.id, label: m.name }))} testId="crm-owner" />
        )}
      </div>
      <TextArea label={t("crm.note")} value={form.note} onChange={(v) => set({ note: v })} optional />
      {d && <Timeline items={d.activities} />}
    </EditorSheet>
  );
}

function Actions({ d, stages, canAssign, marketers, callSeconds, onCalling, onDone }: {
  d: LeadDetail; stages: Stage[]; canAssign: boolean; marketers: Marketer[]; callSeconds?: number; onCalling: (id: string) => void; onDone: (x: LeadDetail) => void;
}) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const l = d.lead;
  const [panel, setPanel] = useState<"call" | "activity" | "follow" | "stage" | "assign" | null>(callSeconds !== undefined ? "call" : null);
  const [converted, setConverted] = useState<string | null>(l.clientId);
  const run = async <T,>(fn: () => Promise<T>, ok: string, after?: (x: T) => void) => {
    try {
      const x = await fn();
      notify.success(ok);
      after?.(x);
      setPanel(null);
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  const btn = (key: NonNullable<typeof panel>, icon: React.ReactNode, label: string) => (
    <Button type="button" variant={panel === key ? "default" : "outline"} className="h-11" onClick={() => setPanel(panel === key ? null : key)} data-testid={`crm-action-${key}`}>
      {icon}
      {label}
    </Button>
  );

  return (
    <section className="space-y-3 rounded-xl border bg-muted/30 p-3">
      <div className="flex flex-wrap gap-2">
        {l.phone && (
          <Button asChild className="h-11">
            <a href={`tel:${l.phone}`} onClick={() => onCalling(l.id)} data-testid="crm-dial">
              <PhoneCall aria-hidden="true" />
              <span dir="ltr">{l.phone}</span>
            </a>
          </Button>
        )}
        {btn("call", <Phone aria-hidden="true" />, t("crm.callResult"))}
        {btn("activity", <MessageSquare aria-hidden="true" />, t("crm.activity"))}
        {btn("follow", <CalendarClock aria-hidden="true" />, t("crm.nextFollowUp"))}
        {btn("stage", <ArrowLeftRight aria-hidden="true" />, t("crm.move"))}
        {canAssign && btn("assign", <UserCheck aria-hidden="true" />, t("crm.assign"))}
        {converted ? (
          <Button asChild variant="outline" className="h-11">
            <Link href="/clients" data-testid="crm-open-client">
              <Briefcase aria-hidden="true" />
              {t("crm.openClient")}
            </Link>
          </Button>
        ) : (
          <Button type="button" variant="outline" className="h-11" data-testid="crm-convert"
            onClick={() => run(() => api<{ clientId: string }>(`/api/v1/crm/leads/${l.id}/convert`, { method: "POST" }), t("crm.converted"), async (x) => {
              setConverted(x.clientId);
              onDone(await api<LeadDetail>(`/api/v1/crm/leads/${l.id}`));
            })}>
            <Briefcase aria-hidden="true" />
            {t("crm.convert")}
          </Button>
        )}
      </div>
      {panel === "call" && <CallForm leadId={l.id} seconds={callSeconds} onSaved={(x) => { onDone(x); setPanel(null); notify.success(t("crm.callSaved")); }} />}
      {panel === "activity" && <ActivityForm leadId={l.id} onSaved={(x) => { onDone(x); setPanel(null); notify.success(t("crm.activitySaved")); }} />}
      {panel === "follow" && (
        <FollowUpPicker value={l.nextFollowUpAt} onPick={(at) => run(() => api<LeadDetail>(`/api/v1/crm/leads/${l.id}/follow-up`, { method: "POST", body: { at } }), t("crud.saved"), onDone)} />
      )}
      {panel === "stage" && <StageForm d={d} stages={stages} onSaved={(x) => { onDone(x); setPanel(null); notify.success(t("crm.moved")); }} />}
      {panel === "assign" && (
        <SelectField label={t("crm.owner")} value={l.ownerId ?? ""} optional testId="crm-assign"
          onChange={(v) => run(() => api<LeadDetail>(`/api/v1/crm/leads/${l.id}/assign`, { method: "POST", body: { ownerId: v || null } }), t("crm.assigned"), onDone)}
          options={marketers.map((m) => ({ value: m.id, label: `${m.name} (${m.openLeads})` }))} />
      )}
    </section>
  );
}

/** Quick picks (in an hour, tomorrow 10:00, in 3 days) or a day + time. */
function FollowUpPicker({ value, onPick }: { value: string | null; onPick: (iso: string | null) => void }) {
  const { t } = useI18n();
  const [day, setDay] = useState<string | null>(value);
  const [time, setTime] = useState("10:00");
  const [now] = useState(() => new Date());
  const tomorrow = new Date(now.getTime() + 24 * 3600_000).toISOString();
  const quick: [string, string][] = [
    ["+۱ ساعت", new Date(now.getTime() + 3600_000).toISOString()],
    ["فردا ۱۰ صبح", tehran(tomorrow, "10:00")],
    ["۳ روز بعد", tehran(new Date(now.getTime() + 3 * 24 * 3600_000).toISOString(), "10:00")],
    ["یک هفته بعد", tehran(new Date(now.getTime() + 7 * 24 * 3600_000).toISOString(), "10:00")],
  ];
  return (
    <div className="space-y-3">
      <p className="text-sm text-muted-foreground">{t("crm.nextFollowUpHelp")}</p>
      <div className="flex flex-wrap gap-2">
        {quick.map(([label, iso]) => (
          <Button key={label} type="button" variant="secondary" className="h-10" onClick={() => onPick(iso)}>{label}</Button>
        ))}
      </div>
      <div className="flex flex-wrap items-end gap-2">
        <div className="w-44"><JalaliDateField label={t("crm.followUpDay")} value={day} onChange={setDay} testId="crm-follow-day" /></div>
        <label className="space-y-1 text-sm font-medium">
          <span className="block">{t("crm.followUpTime")}</span>
          <input type="time" value={time} onChange={(e) => setTime(e.target.value)} className="h-12 rounded-md border bg-background px-2 md:h-11" dir="ltr" data-testid="crm-follow-time" />
        </label>
        <Button type="button" className="h-12 md:h-11" disabled={!day} onClick={() => day && onPick(tehran(day, time))} data-testid="crm-follow-save">{t("common.save")}</Button>
      </div>
    </div>
  );
}

function CallForm({ leadId, seconds, onSaved }: { leadId: string; seconds?: number; onSaved: (x: LeadDetail) => void }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [direction, setDirection] = useState<"outgoing" | "incoming">("outgoing");
  const [minutes, setMinutes] = useState<number | null>(seconds !== undefined ? Math.max(0, Math.round(seconds / 60)) : null);
  const [outcome, setOutcome] = useState<"" | "noAnswer" | "interested" | "later" | "rejected" | "done">("");
  const [note, setNote] = useState("");
  const [follow, setFollow] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const save = async () => {
    if (!outcome) return;
    setBusy(true);
    try {
      onSaved(await api<LeadDetail>(`/api/v1/crm/leads/${leadId}/calls`, {
        method: "POST",
        body: { direction, durationSeconds: minutes != null ? minutes * 60 : null, outcome, note: note || null, nextFollowUpAt: follow },
      }));
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  const outcomes = ["noAnswer", "interested", "later", "rejected", "done"] as const;
  return (
    <div className="space-y-3" data-testid="crm-call-form">
      {seconds !== undefined && <p className="rounded-md bg-amber-100 p-2 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-100">{t("crm.callResultHelp")}</p>}
      <div className="grid gap-3 sm:grid-cols-2">
        <SelectField label={t("crm.direction")} value={direction} onChange={setDirection} options={(["outgoing", "incoming"] as const).map((x) => ({ value: x, label: t(`crm.direction.${x}`) }))} />
        <NumberField label={t("crm.duration")} value={minutes} onChange={setMinutes} optional min={0} max={360} testId="crm-call-minutes" />
      </div>
      <fieldset>
        <legend className="mb-2 text-sm font-bold">{t("crm.outcome")}</legend>
        <div className="flex flex-wrap gap-2">
          {outcomes.map((o) => (
            <button key={o} type="button" onClick={() => setOutcome(o)} aria-pressed={outcome === o} data-testid={`crm-outcome-${o}`}
              className={cn("min-h-11 rounded-full border-2 px-3 text-sm", outcome === o ? "border-foreground bg-foreground text-background" : "border-border")}>
              {t(`crm.outcome.${o}`)}
            </button>
          ))}
        </div>
      </fieldset>
      <TextArea label={t("crm.note")} value={note} onChange={setNote} rows={2} optional testId="crm-call-note" />
      {(outcome === "later" || outcome === "noAnswer" || outcome === "interested") && <FollowUpInline value={follow} onChange={setFollow} />}
      <Button type="button" className="h-11" disabled={!outcome || busy} onClick={save} data-testid="crm-call-save">{t("crm.saveCall")}</Button>
    </div>
  );
}

function FollowUpInline({ value, onChange }: { value: string | null; onChange: (iso: string | null) => void }) {
  const { t } = useI18n();
  const [day, setDay] = useState<string | null>(value);
  const [time, setTime] = useState("10:00");
  return (
    <div className="flex flex-wrap items-end gap-2">
      <div className="w-44"><JalaliDateField label={t("crm.nextFollowUp")} value={day} optional onChange={(v) => { setDay(v); onChange(v ? tehran(v, time) : null); }} /></div>
      <label className="space-y-1 text-sm font-medium">
        <span className="block">{t("crm.followUpTime")}</span>
        <input type="time" value={time} onChange={(e) => { setTime(e.target.value); if (day) onChange(tehran(day, e.target.value)); }} className="h-12 rounded-md border bg-background px-2 md:h-11" dir="ltr" />
      </label>
    </div>
  );
}

function ActivityForm({ leadId, onSaved }: { leadId: string; onSaved: (x: LeadDetail) => void }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [kind, setKind] = useState<"message" | "meeting" | "email" | "note">("message");
  const [channel, setChannel] = useState<"telegram" | "whatsApp" | "sms" | "bale" | "instagram">("whatsApp");
  const [note, setNote] = useState("");
  const [follow, setFollow] = useState<string | null>(null);
  const save = async () => {
    try {
      onSaved(await api<LeadDetail>(`/api/v1/crm/leads/${leadId}/activities`, { method: "POST", body: { kind, channel: kind === "message" ? channel : null, note: note || null, nextFollowUpAt: follow } }));
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  return (
    <div className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <SelectField label={t("crm.activity")} value={kind} onChange={setKind} options={(["message", "meeting", "email", "note"] as const).map((x) => ({ value: x, label: t(`crm.activity.${x}`) }))} />
        {kind === "message" && (
          <SelectField label={t("crm.channel")} value={channel} onChange={setChannel} options={(["whatsApp", "telegram", "bale", "instagram", "sms"] as const).map((x) => ({ value: x, label: t(`crm.channel.${x}`) }))} />
        )}
      </div>
      <TextArea label={t("crm.note")} value={note} onChange={setNote} rows={2} optional />
      <FollowUpInline value={follow} onChange={setFollow} />
      <Button type="button" className="h-11" onClick={save}>{t("crm.saveActivity")}</Button>
    </div>
  );
}

function StageForm({ d, stages, onSaved }: { d: LeadDetail; stages: Stage[]; onSaved: (x: LeadDetail) => void }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [stageId, setStageId] = useState(d.lead.stageId);
  const [reason, setReason] = useState(d.lostReason ?? "");
  const stage = stages.find((s) => s.id === stageId);
  const save = async () => {
    try {
      onSaved(await api<LeadDetail>(`/api/v1/crm/leads/${d.lead.id}/move`, { method: "POST", body: { stageId, lostReason: reason || null } }));
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  return (
    <div className="space-y-3">
      <SelectField label={t("crm.stage")} value={stageId} onChange={setStageId} options={stages.filter((s) => s.isActive).map((s) => ({ value: s.id, label: s.name }))} testId="crm-stage" />
      {stage?.kind === "lost" && <TextField label={t("crm.lostReason")} value={reason} onChange={setReason} testId="crm-lost-reason" />}
      <Button type="button" className="h-11" disabled={stageId === d.lead.stageId && stage?.kind !== "lost"} onClick={save} data-testid="crm-stage-save">{t("crm.move")}</Button>
    </div>
  );
}

function Timeline({ items }: { items: Activity[] }) {
  const { t, formatDate, formatNumber } = useI18n();
  const line = (a: Activity) => {
    switch (a.kind) {
      case "stageChanged": return t("crm.act.stageChanged", { from: a.from ?? "—", to: a.to ?? "—" });
      case "assigned": return a.to ? t("crm.act.assigned", { to: a.to }) : t("crm.act.unassigned");
      case "call": return `${t("crm.act.call")} ${a.direction ? t(`crm.direction.${a.direction}`) : ""}${a.durationSeconds ? ` — ${t("crm.minutes", { count: formatNumber(Math.round(a.durationSeconds / 60)) })}` : ""}${a.outcome ? ` — ${t(`crm.outcome.${a.outcome}`)}` : ""}`;
      case "message": return `${t("crm.act.message")}${a.channel ? ` (${t(`crm.channel.${a.channel}`)})` : ""}`;
      default: return t(`crm.act.${a.kind}` as TextKey);
    }
  };
  return (
    <section>
      <h3 className="mb-2 font-bold">{t("crm.history")}</h3>
      <ol className="space-y-2 border-s-2 ps-4" data-testid="crm-timeline">
        {items.map((a) => (
          <li key={a.id} className="relative text-sm">
            <span className="absolute -start-[1.4rem] top-1.5 size-2.5 rounded-full bg-primary ring-2 ring-background" aria-hidden="true" />
            <p className="font-medium">{line(a)}</p>
            {a.note && a.kind !== "created" && <p className="whitespace-pre-wrap text-muted-foreground">{a.note}</p>}
            <p className="text-xs text-muted-foreground">{formatDate(a.at, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}{a.actorName ? ` — ${a.actorName}` : ""}</p>
          </li>
        ))}
      </ol>
    </section>
  );
}
