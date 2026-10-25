"use client";

import { Check, Copy, Download, ExternalLink } from "lucide-react";
import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faM6 } from "@/lib/i18n/fa-m6";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQuery, useQueryClient } from "@/lib/query";
import { downloadFile } from "@/lib/upload";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";
import { statusTone, type PublishJob } from "@/components/publishing/types";

registerTexts(faM6);
const KEY = ["portal-publishing"];

/** Pages the client runs itself: the file, the caption and the time, then "I published it". Hidden when there's nothing. */
export function PortalPublishing() {
  const { t, formatDate } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const jobs = useQuery({ queryKey: KEY, queryFn: () => api<PublishJob[]>("/api/v1/portal/publishing"), refetchInterval: 60_000 });
  useLive(["publish"], [KEY]);
  const [url, setUrl] = useState<Record<string, string>>({});
  const list = (jobs.data ?? []).filter((j) => j.management === "clientManaged");
  if (list.length === 0) return null;

  const copy = async (j: PublishJob) => {
    const text = [j.caption, j.hashtags.map((h) => "#" + h).join(" ")].filter(Boolean).join("\n\n");
    try {
      await navigator.clipboard.writeText(text);
      notify.success(t("portalPub.copied"));
    } catch {
      notify.error(t("common.errorGeneric"));
    }
  };
  const done = async (j: PublishJob) => {
    try {
      await api(`/api/v1/portal/publishing/${j.id}/published`, { method: "POST", body: { postUrl: url[j.id]?.trim() || null } });
      notify.success(t("portalPub.thanks"));
      await qc.invalidateQueries({ queryKey: KEY });
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  return (
    <section aria-labelledby="pub" className="space-y-3" data-testid="portal-publishing">
      <div>
        <h2 id="pub" className="text-lg font-bold">
          {t("portalPub.title")}
        </h2>
        <p className="text-sm text-muted-foreground">{t("portalPub.help")}</p>
      </div>
      <ul className="space-y-3">
        {list.map((j) => {
          const now = j.status === "due" || j.status === "overdue";
          return (
            <li key={j.id} className={cn("space-y-3 rounded-xl border-2 p-4", statusTone[j.status])} data-testid={`portal-pub-${j.id}`}>
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <p className="font-semibold" dir="auto">
                  {j.platformName} @{j.handle}
                </p>
                <span className={cn("text-sm", now ? "font-bold" : "text-muted-foreground")}>{t(`publish.status.${j.status}` as TextKey)}</span>
              </div>
              <p className="text-sm text-muted-foreground">
                {t("portalPub.at", { at: formatDate(j.publishAt, { weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" }) })}
              </p>
              {j.caption && <p className="rounded-md bg-background/70 p-3 text-sm whitespace-pre-wrap">{j.caption}</p>}
              {j.hashtags.length > 0 && (
                <p className="text-sm font-medium text-sky-700 dark:text-sky-400" dir="auto">
                  {j.hashtags.map((h) => "#" + h).join(" ")}
                </p>
              )}
              <div className="flex flex-wrap gap-2">
                {j.files.map((f) => (
                  <Button key={f.id} variant="outline" className="h-11" onClick={() => downloadFile(f.id, f.fileName)}>
                    <Download className="size-4" aria-hidden="true" />
                    <bdi className="max-w-48 truncate">{f.fileName}</bdi>
                  </Button>
                ))}
                <Button variant="outline" className="h-11" onClick={() => void copy(j)} data-testid="portal-pub-copy">
                  <Copy className="size-4" aria-hidden="true" />
                  {t("portalPub.copy")}
                </Button>
              </div>
              {now && (
                <div className="flex flex-wrap items-center gap-2 border-t pt-3">
                  <label className="sr-only" htmlFor={`u-${j.id}`}>
                    {t("publish.postUrl")}
                  </label>
                  <input
                    id={`u-${j.id}`}
                    dir="ltr"
                    inputMode="url"
                    placeholder={t("publish.postUrl")}
                    className="h-12 min-w-0 flex-1 rounded-md border bg-background px-3 text-base"
                    value={url[j.id] ?? ""}
                    onChange={(e) => setUrl({ ...url, [j.id]: e.target.value })}
                  />
                  <Button className="h-12" onClick={() => void done(j)} data-testid="portal-pub-done">
                    <Check aria-hidden="true" />
                    {t("portalPub.published")}
                  </Button>
                </div>
              )}
              {j.postUrl && (
                <a className="inline-flex items-center gap-1 text-sm font-semibold underline" href={j.postUrl} target="_blank" rel="noopener noreferrer">
                  <ExternalLink className="size-4" aria-hidden="true" />
                  {t("publish.viewPost")}
                </a>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
