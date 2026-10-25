"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { Plus, Trash2 } from "lucide-react";
import dynamic from "next/dynamic";
import { useState } from "react";
import { useAuth } from "@/lib/auth";
import { hasPermission } from "@/lib/types";
import { EditorSheet } from "@/components/app/editor-sheet";
import { useErrorMessage } from "@/components/app/error-text";
import { JalaliDateField, SelectField, TextArea, TextField } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import type { KeysetPage } from "@/lib/types";
import { faM2Forms } from "@/lib/i18n/fa-m2-forms";
import { registerTexts as registerFormTexts } from "@/lib/i18n/registry";

registerFormTexts(faM2Forms);

const Credentials = dynamic(() => import("@/components/vault/credentials").then((m) => m.Credentials), { ssr: false });

type Account = { platformId: string; handle: string; management: string };
type ClientPackage = { id: string; name: string; startsAt: string; endsAt: string; daysLeft: number; totalDays: number; price: number; quotas: { serviceTypeId: string; count: number; serviceName: string | null }[]; used?: Record<string, number> | null };
type Client = {
  id: string;
  businessName: string;
  industry: string | null;
  contactName: string | null;
  mobile: string | null;
  city: string | null;
  internalNote: string | null;
  status: string;
  accounts: Account[];
  hasPortal: boolean;
  activePackage: ClientPackage | null;
  packageHistory: ClientPackage[];
  version: number;
};

const EMPTY: Client = { id: "", businessName: "", industry: null, contactName: null, mobile: "", city: "شیراز", internalNote: null, status: "active", accounts: [], hasPortal: false, activePackage: null, packageHistory: [], version: 0 };

type Props = { id: string; onClose: () => void; onSaved: (created: boolean) => void; onChanged: () => void };

export function ClientEditor(props: Props) {
  const { t } = useI18n();
  const isNew = props.id === "new";
  const existing = useQuery({ queryKey: ["client", props.id], enabled: !isNew, queryFn: () => api<Client>(`/api/v1/clients/${props.id}`), gcTime: 0 });
  if (!isNew && !existing.data) return <EditorSheet title={t("clients.formEdit")} onClose={props.onClose} onSubmit={() => {}} busy={false} error={existing.error} loading={!existing.error} wide>{null}</EditorSheet>;
  return <ClientForm {...props} initial={existing.data ?? EMPTY} />;
}

function ClientForm({ id, onClose, onSaved, onChanged, initial }: Props & { initial: Client }) {
  const { t } = useI18n();
  const { me } = useAuth();
  const qc = useQueryClient();
  const isNew = id === "new";
  const platforms = useQuery({ queryKey: ["platforms"], queryFn: () => api<{ id: string; name: string; isActive: boolean }[]>("/api/v1/platforms") });
  const [c, setC] = useState<Client | null>(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const set = (patch: Partial<Client>) => setC((x) => (x ? { ...x, ...patch } : x));
  const setAccount = (i: number, patch: Partial<Account>) => set({ accounts: c!.accounts.map((a, j) => (j === i ? { ...a, ...patch } : a)) });

  const submit = async () => {
    if (!c) return;
    setBusy(true);
    setError(null);
    try {
      const body = {
        businessName: c.businessName, industry: c.industry, contactName: c.contactName, mobile: c.mobile ?? "", city: c.city, internalNote: c.internalNote,
        status: c.status, accounts: c.accounts.filter((a) => a.platformId && a.handle.trim()), version: isNew ? null : c.version,
      };
      await api(isNew ? "/api/v1/clients" : `/api/v1/clients/${id}`, { method: isNew ? "POST" : "PUT", body });
      onSaved(isNew);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const reload = async (fresh: Client) => {
    setC((x) => (x ? { ...x, version: fresh.version, hasPortal: fresh.hasPortal, activePackage: fresh.activePackage, packageHistory: fresh.packageHistory } : fresh));
    qc.setQueryData(["client", id], fresh);
    onChanged();
  };

  const platformOptions = (platforms.data ?? []).filter((p) => p.isActive).map((p) => ({ value: p.id, label: p.name }));

  return (
    <EditorSheet title={isNew ? t("clients.formCreate") : t("clients.formEdit")} onClose={onClose} onSubmit={submit} busy={busy} error={error} loading={!c} wide testId="client-editor">
      {c && (
        <>
          <div className="grid gap-5 sm:grid-cols-2">
            <TextField label={t("clients.business")} help={t("clients.businessHelp")} value={c.businessName} onChange={(v) => set({ businessName: v })} testId="client-business" />
            <TextField label={t("clients.industry")} help={t("clients.industryHelp")} value={c.industry ?? ""} onChange={(v) => set({ industry: v || null })} optional />
            <TextField label={t("clients.contact")} help={t("clients.contactHelp")} value={c.contactName ?? ""} onChange={(v) => set({ contactName: v || null })} optional />
            {c.mobile !== null && <TextField label={t("clients.mobile")} help={t("clients.mobileHelp")} value={c.mobile} onChange={(v) => set({ mobile: v })} dir="ltr" inputMode="tel" testId="client-mobile" />}
            <TextField label={t("clients.city")} help={t("clients.cityHelp")} value={c.city ?? ""} onChange={(v) => set({ city: v || null })} optional />
            <SelectField label={t("clients.status")} help={t("clients.statusHelp")} value={c.status} onChange={(v) => set({ status: v })} options={(["active", "paused", "ended"] as const).map((s) => ({ value: s, label: t(`clients.status.${s}` as TextKey) }))} />
          </div>
          <TextArea label={t("clients.note")} help={t("clients.noteHelp")} value={c.internalNote ?? ""} onChange={(v) => set({ internalNote: v || null })} optional />

          <section className="space-y-3">
            <h3 className="text-lg font-bold">{t("clients.accounts")}</h3>
            <p className="text-sm text-muted-foreground">{t("clients.accountsHelp")}</p>
            {c.accounts.map((a, i) => (
              <div key={i} className="grid items-end gap-3 rounded-xl border p-3 sm:grid-cols-[1fr_1fr_1fr_auto]" data-testid={`account-${i}`}>
                <SelectField label={t("clients.platform")} help={t("clients.platformHelp")} value={a.platformId} onChange={(v) => setAccount(i, { platformId: v })} options={platformOptions} optional />
                <TextField label={t("clients.handle")} help={t("clients.handleHelp")} value={a.handle} onChange={(v) => setAccount(i, { handle: v })} dir="ltr" />
                <SelectField label={t("clients.management")} help={t("clients.managementHelp")} value={a.management} onChange={(v) => setAccount(i, { management: v })} options={(["agencyManaged", "clientManaged"] as const).map((s) => ({ value: s, label: t(`clients.management.${s}` as TextKey) }))} />
                <Button type="button" variant="ghost" size="icon" className="size-12 text-destructive" aria-label={t("crud.remove")} onClick={() => set({ accounts: c.accounts.filter((_, j) => j !== i) })}>
                  <Trash2 aria-hidden="true" />
                </Button>
              </div>
            ))}
            <Button type="button" variant="outline" className="h-12 md:h-11" onClick={() => set({ accounts: [...c.accounts, { platformId: platformOptions[0]?.value ?? "", handle: "", management: "agencyManaged" }] })} data-testid="account-add">
              <Plus aria-hidden="true" />
              {t("clients.addAccount")}
            </Button>
          </section>

          {!isNew && <PackagesPanel client={c} onChange={reload} />}
          {!isNew && hasPermission(me, "credentials.view", "all") && (
            <div className="rounded-xl border p-4">
              <Credentials mode={{ kind: "staff", clientId: id, canManage: hasPermission(me, "credentials.manage", "all"), canReveal: hasPermission(me, "credentials.reveal", "all") }} accounts={c.accounts} />
            </div>
          )}
        </>
      )}
    </EditorSheet>
  );
}

function PackagesPanel({ client, onChange }: { client: Client; onChange: (c: Client) => void }) {
  const { t, formatNumber, formatDate } = useI18n();
  const errorMessage = useErrorMessage();
  const packages = useQuery({ queryKey: ["packages", "assignable"], queryFn: () => api<KeysetPage<{ id: string; name: string; effectivePrice: number; validityDays: number }>>("/api/v1/packages?status=active&limit=100") });
  const [packageId, setPackageId] = useState("");
  const [start, setStart] = useState<string | null>(null);
  const [working, setWorking] = useState(false);
  const [confirmCancel, setConfirmCancel] = useState(false);

  const run = async (fn: () => Promise<Client>, ok: string) => {
    setWorking(true);
    try {
      onChange(await fn());
      notify.success(ok);
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setWorking(false);
    }
  };

  const ap = client.activePackage;
  const d = (iso: string) => formatDate(iso, { day: "numeric", month: "long", year: "numeric" });

  return (
    <section className="space-y-4 rounded-xl border bg-muted/30 p-4" data-testid="client-packages">
      <h3 className="text-lg font-bold">{t("clients.packagesTitle")}</h3>
      {ap ? (
        <div className="space-y-2 rounded-lg border bg-card p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="font-semibold">{ap.name}</span>
            <span className="text-sm">{t("crud.toman", { amount: formatNumber(ap.price / 10) })}</span>
          </div>
          <p className="text-sm text-muted-foreground">
            {d(ap.startsAt)} — {d(ap.endsAt)}
          </p>
          <div className="h-2 overflow-hidden rounded-full bg-muted" role="progressbar" aria-valuemin={0} aria-valuemax={ap.totalDays} aria-valuenow={ap.daysLeft} aria-label={t("clients.remaining", { total: formatNumber(ap.totalDays), left: formatNumber(ap.daysLeft) })}>
            <div className="h-full bg-primary" style={{ width: `${Math.max(0, Math.min(100, (ap.daysLeft / Math.max(1, ap.totalDays)) * 100))}%` }} />
          </div>
          <p className="text-sm">{t("clients.remaining", { total: formatNumber(ap.totalDays), left: formatNumber(ap.daysLeft) })}</p>
          <ul className="grid gap-1 text-sm sm:grid-cols-2" data-testid="client-quotas">
            {ap.quotas.map((q) => {
              const used = ap.used?.[q.serviceTypeId] ?? 0;
              return (
                <li key={q.serviceTypeId} className={used >= q.count ? "font-semibold text-destructive" : "text-muted-foreground"}>
                  {q.serviceName}: {t("clients.quotaUse", { used: formatNumber(used), total: formatNumber(q.count) })}
                </li>
              );
            })}
          </ul>
          {confirmCancel ? (
            <div className="flex flex-wrap gap-2">
              <Button type="button" variant="destructive" className="h-11" disabled={working} onClick={() => run(() => api<Client>(`/api/v1/clients/${client.id}/packages/${ap.id}/cancel`, { method: "POST" }), t("crud.saved"))} data-testid="package-cancel-yes">
                {t("crud.confirmYes")}
              </Button>
              <Button type="button" variant="ghost" className="h-11" onClick={() => setConfirmCancel(false)}>
                {t("common.cancel")}
              </Button>
            </div>
          ) : (
            <Button type="button" variant="ghost" className="h-11 text-destructive" onClick={() => setConfirmCancel(true)} data-testid="package-cancel">
              {t("clients.cancelPackage")}
            </Button>
          )}
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">{t("clients.noPackage")}</p>
      )}

      <div className="space-y-3">
        <p className="text-sm text-muted-foreground">{t("clients.assignHelp")}</p>
        <div className="grid gap-3 sm:grid-cols-2">
          <SelectField label={t("clients.package")} help={t("clients.packageHelp")} value={packageId} onChange={setPackageId} optional testId="assign-package" options={(packages.data?.items ?? []).map((p) => ({ value: p.id, label: `${p.name} — ${t("crud.toman", { amount: formatNumber(p.effectivePrice / 10) })}` }))} />
          <JalaliDateField label={t("clients.assignStart")} help={t("clients.assignStartHelp")} value={start} onChange={setStart} optional />
        </div>
        <Button type="button" className="h-12 md:h-11" disabled={!packageId || working} onClick={() => run(() => api<Client>(`/api/v1/clients/${client.id}/packages`, { method: "POST", body: { packageId, startsAt: start, note: null } }), t("clients.assigned"))} data-testid="assign-submit">
          {t("clients.assign")}
        </Button>
      </div>

      {client.packageHistory.length > 0 && (
        <details>
          <summary className="cursor-pointer text-sm font-medium">{t("clients.history")}</summary>
          <ul className="mt-2 space-y-1 text-sm text-muted-foreground">
            {client.packageHistory.map((h) => (
              <li key={h.id}>
                {h.name}: {d(h.startsAt)} — {d(h.endsAt)}
              </li>
            ))}
          </ul>
        </details>
      )}

      <div className="border-t pt-3">
        {client.hasPortal ? (
          <p className="text-sm">
            {t("clients.portal")}: <strong>{t("clients.portalOn")}</strong>
          </p>
        ) : (
          <Button type="button" variant="outline" className="h-12 md:h-11" disabled={working} onClick={() => run(() => api<Client>(`/api/v1/clients/${client.id}/portal`, { method: "POST" }), t("clients.portalEnabled"))} data-testid="portal-enable">
            {t("clients.portalEnable")}
          </Button>
        )}
      </div>
    </section>
  );
}
