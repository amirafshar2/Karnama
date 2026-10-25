"use client";

import dynamic from "next/dynamic";
import { FileAudio, FileText, FileVideo, Image as ImageIcon, Inbox, Send } from "lucide-react";
import { useEffect, useState } from "react";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { apiBlob } from "@/lib/api";
import type { Media } from "./types";
import { useAuth } from "@/lib/auth";
import { faN5 } from "@/lib/i18n/fa-n5";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { hasPermission } from "@/lib/types";
import { useKeysetList } from "@/lib/use-keyset";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";

registerTexts(faN5);

const AssignSheet = dynamic(() => import("./assign-sheet").then((m) => m.AssignSheet), { ssr: false });

/** PROMPT2 §8.3 step 7: files sent to the bot, live; assign one and the bot copies it to that person inside Telegram. */
export default function MediaPage() {
  const allowed = useRequire([{ code: "media.inbox", scope: "all" }]);
  const { me } = useAuth();
  const { t } = useI18n();
  const [open, setOpen] = useState(true);
  const [assigning, setAssigning] = useState<Media | null>(null);
  const list = useKeysetList<Media>("media", "/api/v1/media", { open: open ? "true" : undefined, limit: "40" }, allowed);
  useLive(["media"], [["media"]]);
  const canAssign = hasPermission(me, "media.assign", "all");
  // Opened from the bot's "open in Karnama" button: highlight that file.
  const [focus] = useState<string | null>(() => (typeof window === "undefined" ? null : new URLSearchParams(window.location.search).get("id")));
  if (!allowed) return null;

  return (
    <div className="space-y-6">
      <header>
        <h1 className="flex items-center gap-2 text-2xl font-extrabold">
          <Inbox className="size-6 text-primary" aria-hidden="true" />
          {t("media.title")}
        </h1>
        <p className="mt-1 max-w-prose text-sm text-muted-foreground">{t("media.subtitle")}</p>
      </header>
      <div className="flex flex-wrap gap-2" role="tablist">
        {[true, false].map((v) => (
          <button key={String(v)} role="tab" aria-selected={open === v} onClick={() => setOpen(v)} data-testid={`media-filter-${v ? "open" : "all"}`}
            className={cn("min-h-11 rounded-full border-2 px-4 text-sm font-semibold", open === v ? "border-foreground bg-foreground text-background" : "border-border hover:border-foreground")}>
            {t(v ? "media.filter.open" : "media.filter.all")}
          </button>
        ))}
      </div>
      {!list.isLoading && list.items.length === 0 && <p className="rounded-xl border border-dashed p-10 text-center text-muted-foreground">{t("media.empty")}</p>}
      <ul className="grid gap-3 lg:grid-cols-2" data-testid="media-list">
        {list.items.map((m) => (
          <MediaCard key={m.id} m={m} focused={focus === m.id} canAssign={canAssign} onAssign={() => setAssigning(m)} />
        ))}
      </ul>
      {list.hasMore && <Button variant="outline" onClick={list.loadMore}>{t("common.loadMore")}</Button>}
      {assigning && (
        <AssignSheet
          media={assigning}
          onClose={() => setAssigning(null)}
          onDone={async () => {
            setAssigning(null);
            notify.success(t("media.assigned"));
            await list.refresh();
          }}
        />
      )}
    </div>
  );
}

function KindIcon({ kind }: { kind: Media["kind"] }) {
  const Icon = kind === "photo" ? ImageIcon : kind === "audio" || kind === "voice" ? FileAudio : kind === "document" ? FileText : FileVideo;
  return <Icon className="size-8 text-muted-foreground" aria-hidden="true" />;
}

function Thumb({ m }: { m: Media }) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    if (!m.hasThumb) return;
    let gone = false;
    let made: string | null = null;
    apiBlob(`/api/v1/media/${m.id}/thumb`)
      .then((b) => {
        if (gone) return;
        made = URL.createObjectURL(b);
        setUrl(made);
      })
      .catch(() => undefined);
    return () => {
      gone = true;
      if (made) URL.revokeObjectURL(made);
    };
  }, [m.id, m.hasThumb]);
  return (
    <div className="flex size-20 shrink-0 items-center justify-center overflow-hidden rounded-lg bg-muted">
      {/* eslint-disable-next-line @next/next/no-img-element -- private blob preview */}
      {url ? <img src={url} alt="" className="size-full object-cover" /> : <KindIcon kind={m.kind} />}
    </div>
  );
}

const STATUS_TONE: Record<Media["status"], string> = {
  received: "border",
  archived: "border",
  sentToAdmins: "bg-secondary text-secondary-foreground",
  assigned: "bg-amber-100 text-amber-950 dark:bg-amber-950 dark:text-amber-100",
  delivered: "bg-emerald-100 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100",
  failed: "bg-destructive/15 text-destructive",
};

function MediaCard({ m, focused, canAssign, onAssign }: { m: Media; focused: boolean; canAssign: boolean; onAssign: () => void }) {
  const { t, formatNumber, formatDate } = useI18n();
  const time = (iso: string) => formatDate(iso, { hour: "2-digit", minute: "2-digit", day: "numeric", month: "short" });
  const size = m.fileSize == null ? null : m.fileSize >= 1048576 ? t("media.mb", { size: formatNumber(Math.round(m.fileSize / 1048576)) }) : t("media.kb", { size: formatNumber(Math.max(1, Math.round(m.fileSize / 1024))) });
  const duration = m.durationSeconds == null ? null : `${formatNumber(Math.floor(m.durationSeconds / 60))}:${formatNumber(m.durationSeconds % 60).padStart(2, formatNumber(0))}`;
  return (
    <li className={cn("flex gap-3 rounded-xl border bg-card p-4", focused && "ring-2 ring-primary")} data-testid={`media-${m.id}`}>
      <Thumb m={m} />
      <div className="min-w-0 flex-1 space-y-1">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <p className="font-semibold">
            {t(`media.kind.${m.kind}` as TextKey)}
            {[size, duration].filter(Boolean).length > 0 && <span className="font-normal text-muted-foreground"> — {[size, duration].filter(Boolean).join("، ")}</span>}
          </p>
          <span className={cn("rounded-md px-2 py-0.5 text-xs font-medium", STATUS_TONE[m.status])} data-testid="media-status">{t(`media.status.${m.status}` as TextKey)}</span>
        </div>
        <p className="text-sm">{t("media.from", { name: m.senderName })}</p>
        <p className="text-xs text-muted-foreground">
          {t("media.sentAt", { time: time(m.sentAt) })} — {t("media.receivedAt", { time: time(m.receivedAt) })}
        </p>
        {m.caption && <p className="line-clamp-2 text-sm text-muted-foreground">{m.caption}</p>}
        <p className="text-sm">
          <span className="text-muted-foreground">{t("media.work")}: </span>
          {m.workTitle ? `${m.workTitle}${m.orderNumber ? ` (#${formatNumber(m.orderNumber)})` : ""}${m.clientName ? ` — ${m.clientName}` : ""}` : <span className="text-muted-foreground">{t("media.noWork")}</span>}
        </p>
        {m.assignedToName && <p className="text-sm font-medium">{t("media.assignedTo", { name: m.assignedToName })}</p>}
        {m.status === "failed" && m.error && <p className="text-xs text-destructive" dir="ltr">{m.error}</p>}
        {canAssign && (
          <Button type="button" variant={m.status === "delivered" ? "outline" : "default"} className="mt-1 h-11" onClick={onAssign} data-testid="media-assign">
            <Send aria-hidden="true" />
            {m.assignedToId ? t("media.reassign") : t("media.assign")}
          </Button>
        )}
      </div>
    </li>
  );
}
