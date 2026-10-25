"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { CalendarClock, RefreshCw } from "lucide-react";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { TextArea } from "@/components/app/form";
import { Credentials } from "@/components/vault/credentials";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faN4 } from "@/lib/i18n/fa-n4";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";

registerTexts(faN4);

type Profile = {
  businessName: string;
  industry: string | null;
  contactName: string | null;
  city: string | null;
  accounts: { platformId: string; platformName: string; handle: string }[];
  package: { name: string; startsAt: string; endsAt: string; daysLeft: number; totalDays: number; quotas: { serviceTypeId: string; serviceName: string; used: number; total: number }[] } | null;
  history: { name: string; startsAt: string; endsAt: string; status: "active" | "ended" | "cancelled" }[];
  renewRequestedToday: boolean;
  needsPassword: boolean;
};

/** PROMPT2 §10.3: the client's profile — business, pages, running package (days + quotas), history, renewal, logins. */
export function PortalProfile() {
  const { t, formatNumber, formatDate } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const profile = useQuery({ queryKey: ["portal-profile"], queryFn: () => api<Profile>("/api/v1/portal/profile") });
  const [renewOpen, setRenewOpen] = useState(false);
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const p = profile.data;
  const d = (iso: string) => formatDate(iso, { day: "numeric", month: "long", year: "numeric" });

  const renew = async () => {
    setBusy(true);
    try {
      const r = await api<{ alreadyRequested: boolean }>("/api/v1/portal/renew", { method: "POST", body: { note: note.trim() || null } });
      notify.success(r.alreadyRequested ? t("profileClient.renewAlready") : t("profileClient.renewDone"));
      setRenewOpen(false);
      setNote("");
      await qc.invalidateQueries({ queryKey: ["portal-profile"] });
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  if (!p) return <div className="h-48 animate-pulse rounded-xl bg-muted" />;
  const pk = p.package;
  return (
    <div className="space-y-6" data-testid="portal-profile">
      <section className="rounded-xl border bg-card p-4 sm:p-5">
        <h2 className="text-xl font-extrabold">{p.businessName}</h2>
        <p className="text-sm text-muted-foreground">{[p.industry, p.city, p.contactName].filter(Boolean).join("، ")}</p>
        {p.accounts.length > 0 && (
          <ul className="mt-3 flex flex-wrap gap-2 text-sm">
            {p.accounts.map((a) => (
              <li key={`${a.platformId}-${a.handle}`} className="rounded-md border px-2 py-1">
                {a.platformName} · <bdi dir="ltr">@{a.handle}</bdi>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className="space-y-3 rounded-xl border bg-card p-4 sm:p-5" data-testid="profile-package">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className="text-lg font-bold">{t("profileClient.package")}</h3>
            {pk ? (
              <>
                <p className="text-base font-semibold">{pk.name}</p>
                <p className="text-sm text-muted-foreground">{t("profileClient.period", { from: d(pk.startsAt), to: d(pk.endsAt) })}</p>
              </>
            ) : (
              <p className="text-sm text-muted-foreground">{t("profileClient.noPackage")}</p>
            )}
          </div>
          {p.renewRequestedToday ? (
            <p className="rounded-md bg-secondary px-3 py-2 text-sm text-secondary-foreground" data-testid="renew-done">{t("profileClient.renewAlready")}</p>
          ) : (
            !renewOpen && (
              <Button type="button" className="h-12 md:h-11" onClick={() => setRenewOpen(true)} data-testid="renew-open">
                <RefreshCw aria-hidden="true" />
                {t("profileClient.renew")}
              </Button>
            )
          )}
        </div>

        {pk && (
          <>
            <div className="h-3 overflow-hidden rounded-full bg-muted" role="progressbar" aria-valuemin={0} aria-valuemax={pk.totalDays} aria-valuenow={pk.daysLeft} aria-label={t("profileClient.daysLeft", { days: formatNumber(pk.daysLeft) })}>
              <div className={`h-full ${pk.daysLeft <= 3 ? "bg-destructive" : "bg-primary"}`} style={{ width: `${Math.max(2, Math.min(100, (pk.daysLeft / Math.max(1, pk.totalDays)) * 100))}%` }} />
            </div>
            <p className={`flex items-center gap-1.5 text-sm font-medium ${pk.daysLeft <= 3 ? "text-destructive" : ""}`}>
              <CalendarClock className="size-4" aria-hidden="true" />
              {pk.daysLeft === 0 ? t("profileClient.lastDay") : t("profileClient.daysLeft", { days: formatNumber(pk.daysLeft) })}
            </p>
            {pk.quotas.length > 0 && (
              <div>
                <h4 className="mb-2 text-sm font-bold">{t("profileClient.quotas")}</h4>
                <ul className="grid gap-2 sm:grid-cols-2">
                  {pk.quotas.map((q) => {
                    const left = Math.max(0, q.total - q.used);
                    return (
                      <li key={q.serviceTypeId} className="rounded-lg border p-3">
                        <div className="flex items-baseline justify-between gap-2">
                          <span className="font-medium">{q.serviceName}</span>
                          <span className={`text-sm ${left === 0 ? "font-bold text-destructive" : "text-muted-foreground"}`}>{t("profileClient.quotaLeft", { left: formatNumber(left) })}</span>
                        </div>
                        <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-muted">
                          <div className="h-full bg-primary" style={{ width: `${Math.min(100, (q.used / Math.max(1, q.total)) * 100)}%` }} />
                        </div>
                        <p className="mt-1 text-xs text-muted-foreground">{t("profileClient.quotaLine", { used: formatNumber(q.used), total: formatNumber(q.total) })}</p>
                      </li>
                    );
                  })}
                </ul>
              </div>
            )}
          </>
        )}

        {renewOpen && (
          <div className="space-y-3 rounded-lg border bg-muted/40 p-3" data-testid="renew-form">
            <TextArea label={t("profileClient.renewNote")} help={t("profileClient.renewNoteHelp")} value={note} onChange={setNote} rows={2} optional testId="renew-note" />
            <div className="flex flex-wrap gap-2">
              <Button type="button" className="h-11" disabled={busy} onClick={renew} data-testid="renew-send">
                {t("profileClient.renewSend")}
              </Button>
              <Button type="button" variant="ghost" className="h-11" onClick={() => setRenewOpen(false)}>
                {t("common.cancel")}
              </Button>
            </div>
          </div>
        )}

        {p.history.length > 0 && (
          <details className="text-sm">
            <summary className="cursor-pointer font-medium">{t("profileClient.history")}</summary>
            <ul className="mt-2 space-y-1">
              {p.history.map((h, i) => (
                <li key={i} className="flex flex-wrap justify-between gap-2 border-b py-1.5 last:border-0">
                  <span>{h.name}</span>
                  <span className="text-muted-foreground">
                    {d(h.startsAt)} — {d(h.endsAt)} · {t(`profileClient.status.${h.status}` as TextKey)}
                  </span>
                </li>
              ))}
            </ul>
          </details>
        )}
      </section>

      <section className="space-y-3 rounded-xl border bg-card p-4 sm:p-5">
        {p.needsPassword && <p className="rounded-lg bg-amber-100 p-3 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-100">{t("profileClient.passwordNeeded")}</p>}
        <Credentials mode={{ kind: "portal" }} accounts={p.accounts} />
      </section>
    </div>
  );
}
