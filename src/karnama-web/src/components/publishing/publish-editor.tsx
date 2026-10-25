"use client";

import { Ban, CheckCircle2 } from "lucide-react";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { useErrorMessage } from "@/components/app/error-text";
import { JalaliDateField, SelectField, TextArea, TextField } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import { useQuery } from "@/lib/query";
import type { KeysetPage } from "@/lib/types";
import type { OrderDetail } from "@/lib/work-types";
import { atTehran, splitTehran, type PublishJob } from "./types";

type ClientDetail = { id: string; businessName: string; accounts: { platformId: string; handle: string; management: "agencyManaged" | "clientManaged" }[] };
type Platform = { id: string; name: string };

/** Create or edit one scheduled post. Opened from the calendar, or from an order ("schedule publishing"). */
export function PublishEditor({ job, initial, onClose, onSaved }: { job?: PublishJob; initial?: { clientId?: string; orderId?: string; day?: string }; onClose: () => void; onSaved: () => void }) {
  const { t, formatNumber } = useI18n();
  const errorMessage = useErrorMessage();
  const start = job ? splitTehran(job.publishAt) : { day: initial?.day ?? "", time: "18:00" };
  const [clientId, setClientId] = useState(job?.clientId ?? initial?.clientId ?? "");
  const [account, setAccount] = useState(job ? `${job.platformId}|${job.handle}` : "");
  const [day, setDay] = useState<string | null>(start.day ? `${start.day}T00:00:00+03:30` : null);
  const [time, setTime] = useState(start.time);
  const [orderId, setOrderId] = useState(job?.orderId ?? initial?.orderId ?? "");
  const [fileIds, setFileIds] = useState<string[]>(job?.files.map((f) => f.id) ?? []);
  const [caption, setCaption] = useState(job?.caption ?? "");
  const [tags, setTags] = useState(job?.hashtags.map((h) => "#" + h).join(" ") ?? "");
  const [postUrl, setPostUrl] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const clients = useQuery({ queryKey: ["clients", "options"], queryFn: () => api<KeysetPage<{ id: string; businessName: string }>>("/api/v1/clients?limit=100&status=active") });
  const client = useQuery({ queryKey: ["client", clientId], enabled: !!clientId, queryFn: () => api<ClientDetail>(`/api/v1/clients/${clientId}`) });
  const platforms = useQuery({ queryKey: ["platforms"], queryFn: () => api<Platform[]>("/api/v1/platforms") });
  const orders = useQuery({
    queryKey: ["orders", "of", clientId],
    enabled: !!clientId,
    queryFn: () => api<KeysetPage<{ id: string; number: number; title: string }>>(`/api/v1/orders?limit=50&clientId=${clientId}`),
  });
  const order = useQuery({ queryKey: ["order", orderId], enabled: !!orderId, queryFn: () => api<OrderDetail>(`/api/v1/orders/${orderId}`) });
  const platformName = (id: string) => platforms.data?.find((p) => p.id === id)?.name ?? "";
  const accounts = client.data?.accounts ?? [];
  const chosen = accounts.find((a) => `${a.platformId}|${a.handle}` === account);
  const deliverables = (order.data?.files ?? []).filter((f) => f.purpose === "deliverable");
  const closed = job && (job.status === "published" || job.status === "cancelled");

  const submit = async () => {
    setError(null);
    if (!chosen || !day) return notify.error(t("publish.required"));
    setBusy(true);
    try {
      const body = {
        clientId,
        orderId: orderId || null,
        platformId: chosen.platformId,
        handle: chosen.handle,
        publishAt: atTehran(day.slice(0, 10), time),
        caption,
        hashtags: tags.split(/[\s,،]+/).filter(Boolean),
        fileIds,
        version: job?.version,
      };
      await api(job ? `/api/v1/publishing/${job.id}` : "/api/v1/publishing", { method: job ? "PUT" : "POST", body });
      notify.success(t("publish.saved"));
      onSaved();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const act = async (path: string, body: unknown, done: string) => {
    try {
      await api(`/api/v1/publishing/${job!.id}/${path}`, { method: "POST", body });
      notify.success(done);
      onSaved();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  return (
    <EditorSheet title={job ? t("publish.edit") : t("publish.new")} onClose={onClose} onSubmit={closed ? onClose : submit} busy={busy} error={error} wide testId="publish-editor">
      <div className="grid gap-5 sm:grid-cols-2">
        <SelectField
          label={t("publish.client")}
          value={clientId}
          onChange={(v) => {
            setClientId(v);
            setAccount("");
            setOrderId("");
            setFileIds([]);
          }}
          options={(clients.data?.items ?? []).map((c) => ({ value: c.id, label: c.businessName }))}
          testId="publish-client"
        />
        <SelectField
          label={t("publish.page")}
          help={t("publish.pageHelp")}
          value={account}
          onChange={setAccount}
          options={accounts.map((a) => ({
            value: `${a.platformId}|${a.handle}`,
            label: `${platformName(a.platformId)} @${a.handle} — ${a.management === "clientManaged" ? t("publish.clientManaged") : t("publish.agencyManaged")}`,
          }))}
          testId="publish-page"
        />
        <JalaliDateField label={t("publish.day")} value={day} onChange={setDay} testId="publish-day" />
        <TextField label={t("publish.time")} type="time" dir="ltr" value={time} onChange={setTime} testId="publish-time" />
      </div>
      <SelectField
        label={t("publish.order")}
        value={orderId}
        optional
        onChange={(v) => {
          setOrderId(v);
          setFileIds([]);
        }}
        options={(orders.data?.items ?? []).map((o) => ({ value: o.id, label: `#${formatNumber(o.number)} — ${o.title}` }))}
        testId="publish-order"
      />
      {orderId && (
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">{t("publish.files")}</legend>
          <p className="text-xs text-muted-foreground">{t("publish.filesHelp")}</p>
          {deliverables.length === 0 && <p className="text-sm text-muted-foreground">{t("publish.noFiles")}</p>}
          <ul className="space-y-1">
            {deliverables.map((f) => (
              <li key={f.id}>
                <label className="flex min-h-11 items-center gap-3 rounded-md border px-3">
                  <input
                    type="checkbox"
                    className="size-5 accent-[var(--primary)]"
                    checked={fileIds.includes(f.id)}
                    onChange={(e) => setFileIds(e.target.checked ? [...fileIds, f.id] : fileIds.filter((x) => x !== f.id))}
                  />
                  <bdi className="truncate">{f.fileName}</bdi>
                </label>
              </li>
            ))}
          </ul>
        </fieldset>
      )}
      <TextArea label={t("publish.caption")} value={caption} onChange={setCaption} rows={6} testId="publish-caption" />
      <TextField label={t("publish.hashtags")} help={t("publish.hashtagsHelp")} value={tags} onChange={setTags} optional testId="publish-tags" />

      {job && !closed && (
        <div className="space-y-3 rounded-lg border p-4">
          {job.management === "agencyManaged" && (
            <div className="flex flex-wrap items-end gap-2">
              <div className="min-w-56 flex-1">
                <TextField label={t("publish.postUrl")} dir="ltr" value={postUrl} onChange={setPostUrl} optional placeholder="https://" testId="publish-url" />
              </div>
              <Button type="button" className="h-12" onClick={() => void act("published", { postUrl: postUrl || null }, t("publish.done"))} data-testid="publish-mark">
                <CheckCircle2 aria-hidden="true" />
                {t("publish.markPublished")}
              </Button>
            </div>
          )}
          <Button type="button" variant="ghost" className="h-11 text-destructive" onClick={() => void act("cancel", {}, t("publish.cancelled"))} data-testid="publish-cancel">
            <Ban aria-hidden="true" />
            {t("publish.cancel")}
          </Button>
        </div>
      )}
    </EditorSheet>
  );
}
