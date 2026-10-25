"use client";

import { Bell } from "lucide-react";
import dynamic from "next/dynamic";
import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import { useQuery } from "@/lib/query";
import { useLive } from "@/lib/use-live";

export type InboxItem = { id: string; kind: string; title: string; body: string | null; link: string | null; important: boolean; createdAt: string; read: boolean };
export type Inbox = { unread: number; items: InboxItem[] };
export const INBOX = ["inbox"];

// The list itself is fetched only when someone opens it.
const BellPanel = dynamic(() => import("./bell-panel").then((m) => m.BellPanel), { ssr: false });

/** Header bell: unread count, live; a new notification also shows a toast. The list opens on demand. */
export function BellButton() {
  const { t, formatNumber } = useI18n();
  const [open, setOpen] = useState(false);
  // Live deltas cover the API; reminders from the background worker arrive with the one-minute refresh.
  const inbox = useQuery({ queryKey: INBOX, queryFn: () => api<Inbox>("/api/v1/notifications"), refetchInterval: 60_000 });
  useLive(["notification"], [INBOX]);
  const lastTop = useRef<string | null | undefined>(undefined);
  const top = inbox.data?.items[0];

  useEffect(() => {
    if (!inbox.data) return;
    const id = top?.id ?? null;
    if (lastTop.current !== undefined && id && id !== lastTop.current && top && !top.read) notify.message(top.title, { duration: 6000 });
    lastTop.current = id;
  }, [inbox.data, top]);

  const unread = inbox.data?.unread ?? 0;
  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen(!open)}
        aria-expanded={open}
        aria-label={unread > 0 ? `${t("bell.open")} — ${t("bell.unread", { count: formatNumber(unread) })}` : t("bell.open")}
        className="relative grid size-11 place-items-center rounded-full hover:bg-muted focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        data-testid="bell"
      >
        <Bell className="size-5" aria-hidden="true" />
        {unread > 0 && (
          <span className="absolute -top-0.5 -end-0.5 grid min-w-5 place-items-center rounded-full bg-destructive px-1 text-[11px] leading-5 font-bold text-white" data-testid="bell-count">
            {unread > 99 ? "۹۹+" : formatNumber(unread)}
          </span>
        )}
      </button>
      {open && <BellPanel inbox={inbox.data} onClose={() => setOpen(false)} />}
    </div>
  );
}
