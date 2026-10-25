"use client";

import { Check, ExternalLink, Loader2, Unlink } from "lucide-react";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faM6 } from "@/lib/i18n/fa-m6";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQuery, useQueryClient } from "@/lib/query";

registerTexts(faM6);

type Channel = "inApp" | "sms" | "bale" | "telegram" | "push";
type Messenger = "bale" | "telegram";
type Prefs = { channels: Channel[]; muted: string[]; quietFrom: string | null; quietTo: string | null; linked: Messenger[]; baleAvailable: boolean; telegramAvailable: boolean };
const KEY = ["notify-prefs"];
const hhmm = (t: string | null) => (t ? t.slice(0, 5) : "");

/** Profile section: where else to hear from us (Bale, Telegram, SMS for important things) and quiet hours. */
export function NotifyPrefs() {
  const { t } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const prefs = useQuery({ queryKey: KEY, queryFn: () => api<Prefs>("/api/v1/notifications/prefs") });
  const [link, setLink] = useState<{ m: Messenger; url: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const p = prefs.data;
  if (!p) return <div className="h-40 animate-pulse rounded-xl bg-muted" />;

  const save = async (patch: Partial<Prefs>) => {
    const next = { ...p, ...patch };
    setBusy(true);
    try {
      const saved = await api<Prefs>("/api/v1/notifications/prefs", {
        method: "PUT",
        body: { channels: next.channels, muted: next.muted, quietFrom: next.quietFrom || null, quietTo: next.quietTo || null },
      });
      qc.setQueryData(KEY, saved);
      notify.success(t("notifyPrefs.saved"));
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };
  const toggle = (c: Channel, on: boolean) => save({ channels: on ? [...new Set([...p.channels, c])] : p.channels.filter((x) => x !== c) });

  const connect = async (m: Messenger) => {
    try {
      const r = await api<{ url: string }>(`/api/v1/notifications/links/${m}`, { method: "POST" });
      setLink({ m, url: r.url });
      if (!p.channels.includes(m)) await toggle(m, true);
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  const disconnect = async (m: Messenger) => {
    await api(`/api/v1/notifications/links/${m}`, { method: "DELETE" }).catch((e) => notify.error(errorMessage(e)));
    await qc.invalidateQueries({ queryKey: KEY });
  };

  const messenger = (m: Messenger, available: boolean) => {
    const linked = p.linked.includes(m);
    return (
      <li key={m} className="space-y-3 rounded-lg border p-4" data-testid={`prefs-${m}`}>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <label className="flex min-h-11 items-center gap-3">
            <input type="checkbox" className="size-5 accent-[var(--primary)]" checked={linked && p.channels.includes(m)} disabled={!linked || busy} onChange={(e) => void toggle(m, e.target.checked)} />
            <span className="font-semibold">{t(`notifyPrefs.${m}` as TextKey)}</span>
            <span className={linked ? "text-sm text-emerald-700 dark:text-emerald-400" : "text-sm text-muted-foreground"}>
              {linked ? t("notifyPrefs.connected") : t("notifyPrefs.notConnected")}
            </span>
          </label>
          {!available ? (
            <span className="text-sm text-muted-foreground">{t("notifyPrefs.unavailable")}</span>
          ) : linked ? (
            <Button variant="ghost" className="h-11" onClick={() => void disconnect(m)}>
              <Unlink aria-hidden="true" />
              {t("notifyPrefs.disconnect")}
            </Button>
          ) : (
            <Button variant="outline" className="h-11" onClick={() => void connect(m)} data-testid={`connect-${m}`}>
              {t("notifyPrefs.connect")}
            </Button>
          )}
        </div>
        {link?.m === m && !linked && (
          <div className="space-y-2 rounded-md bg-muted/60 p-3 text-sm">
            <p>{t("notifyPrefs.linkHelp")}</p>
            <div className="flex flex-wrap gap-2">
              <Button asChild className="h-11">
                <a href={link.url} target="_blank" rel="noopener noreferrer" data-testid={`bot-link-${m}`}>
                  <ExternalLink aria-hidden="true" />
                  {t("notifyPrefs.openBot")}
                </a>
              </Button>
              <Button variant="outline" className="h-11" onClick={() => void prefs.refetch()}>
                <Check aria-hidden="true" />
                {t("notifyPrefs.checkAgain")}
              </Button>
            </div>
          </div>
        )}
      </li>
    );
  };

  return (
    <section id="notifications" className="space-y-4 scroll-mt-20" aria-labelledby="np-title" data-testid="notify-prefs">
      <div>
        <h2 id="np-title" className="font-bold">
          {t("notifyPrefs.title")} {busy && <Loader2 className="inline size-4 animate-spin" aria-hidden="true" />}
        </h2>
        <p className="text-sm text-muted-foreground">{t("notifyPrefs.help")}</p>
      </div>
      <ul className="space-y-2">
        {messenger("bale", p.baleAvailable)}
        {messenger("telegram", p.telegramAvailable)}
        <li className="rounded-lg border p-4">
          <label className="flex min-h-11 items-center gap-3">
            <input type="checkbox" className="size-5 accent-[var(--primary)]" checked={p.channels.includes("sms")} disabled={busy} onChange={(e) => void toggle("sms", e.target.checked)} data-testid="prefs-sms" />
            <span className="font-semibold">{t("notifyPrefs.sms")}</span>
          </label>
        </li>
      </ul>
      <fieldset className="space-y-2 rounded-lg border p-4">
        <legend className="px-1 font-semibold">{t("notifyPrefs.quiet")}</legend>
        <p className="text-sm text-muted-foreground">{t("notifyPrefs.quietHelp")}</p>
        <div className="flex flex-wrap gap-4">
          {(["quietFrom", "quietTo"] as const).map((k) => (
            <label key={k} className="flex items-center gap-2 text-sm">
              {t(`notifyPrefs.${k}` as TextKey)}
              <input
                type="time"
                dir="ltr"
                className="h-11 rounded-md border bg-transparent px-2 text-base"
                defaultValue={hhmm(p[k])}
                onBlur={(e) => e.target.value !== hhmm(p[k]) && void save({ [k]: e.target.value ? `${e.target.value}:00` : null })}
                data-testid={`prefs-${k}`}
              />
            </label>
          ))}
        </div>
      </fieldset>
    </section>
  );
}
