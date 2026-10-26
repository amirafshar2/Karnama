"use client";

import { AlertCircle, Settings } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import { faM6 } from "@/lib/i18n/fa-m6";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { useQueryClient } from "@/lib/query";
import { cn } from "@/lib/utils";
import { INBOX, type Inbox } from "./bell";

registerTexts(faM6);

/** Only app-relative links are followed (the server only stores those; this is the second lock). */
const safeLink = (l: string | null) => (l && l.startsWith("/") && !l.startsWith("//") ? l : null);

export function BellPanel({ inbox, onClose }: { inbox: Inbox | undefined; onClose: () => void }) {
  const { t, formatDate } = useI18n();
  const qc = useQueryClient();
  const router = useRouter();
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    ref.current?.focus();
    const key = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    const click = (e: MouseEvent) => {
      const el = e.target as Node;
      if (ref.current && !ref.current.contains(el) && !(el as HTMLElement).closest?.("[data-testid=bell]")) onClose();
    };
    document.addEventListener("keydown", key);
    document.addEventListener("mousedown", click);
    return () => {
      document.removeEventListener("keydown", key);
      document.removeEventListener("mousedown", click);
    };
  }, [onClose]);

  const read = async (id: string | null) => {
    await api("/api/v1/notifications/read", { method: "POST", body: { id } }).catch(() => undefined);
    await qc.invalidateQueries({ queryKey: INBOX });
  };

  // Older notifications, 30 at a time (the bell itself always shows the newest 30).
  const [older, setOlder] = useState<Inbox["items"]>([]);
  const [more, setMore] = useState(true);
  const [loading, setLoading] = useState(false);
  const loadOlder = async () => {
    const all = [...(inbox?.items ?? []), ...older];
    const last = all[all.length - 1];
    if (!last) return;
    setLoading(true);
    try {
      const page = await api<Inbox>(`/api/v1/notifications?before=${encodeURIComponent(last.createdAt)}`);
      setOlder((o) => [...o, ...page.items]);
      setMore(page.items.length === 30);
    } finally {
      setLoading(false);
    }
  };
  const items = [...(inbox?.items ?? []), ...older];
  return (
    <div
      ref={ref}
      tabIndex={-1}
      role="dialog"
      aria-label={t("bell.title")}
      className="fixed inset-x-2 top-16 z-30 max-h-[70dvh] overflow-hidden rounded-xl border bg-popover text-popover-foreground shadow-lg outline-none sm:absolute sm:inset-x-auto sm:end-0 sm:top-12 sm:w-96"
      data-testid="bell-panel"
    >
      <div className="flex items-center justify-between gap-2 border-b px-4 py-3">
        <h2 className="font-bold">{t("bell.title")}</h2>
        <div className="flex items-center gap-1">
          {(inbox?.unread ?? 0) > 0 && (
            <button type="button" className="min-h-10 rounded-md px-2 text-sm font-semibold underline underline-offset-4 hover:bg-muted" onClick={() => void read(null)} data-testid="bell-read-all">
              {t("bell.readAll")}
            </button>
          )}
          <Link href="/profile#notifications" onClick={onClose} className="grid size-10 place-items-center rounded-md hover:bg-muted" aria-label={t("bell.settings")}>
            <Settings className="size-4" aria-hidden="true" />
          </Link>
        </div>
      </div>
      <ul className="max-h-[calc(70dvh-3.5rem)] divide-y overflow-y-auto">
        {items.length === 0 && <li className="p-6 text-center text-sm text-muted-foreground">{t("bell.empty")}</li>}
        {items.map((n) => (
          <li key={n.id}>
            <button
              type="button"
              className={cn("flex w-full gap-3 px-4 py-3 text-start hover:bg-muted/60", !n.read && "bg-accent/30")}
              onClick={async () => {
                await read(n.id);
                const l = safeLink(n.link);
                onClose();
                if (l) router.push(l);
              }}
              data-testid="bell-item"
            >
              <span className={cn("mt-1.5 size-2 shrink-0 rounded-full", n.read ? "bg-transparent" : "bg-sky-600")} aria-hidden="true" />
              <span className="min-w-0 flex-1">
                <span className="flex items-center gap-1 font-semibold">
                  {n.important && <AlertCircle className="size-4 shrink-0 text-destructive" aria-label={t("bell.important")} />}
                  <span className="truncate">{n.title}</span>
                </span>
                {n.body && <span className="mt-0.5 line-clamp-2 block text-sm text-muted-foreground">{n.body}</span>}
                <span className="mt-1 block text-xs text-muted-foreground">{formatDate(n.createdAt, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}</span>
              </span>
            </button>
          </li>
        ))}
        {items.length >= 30 && more && (
          <li className="p-2 text-center">
            <button type="button" disabled={loading} onClick={() => void loadOlder()} className="min-h-10 w-full rounded-md text-sm font-semibold hover:bg-muted" data-testid="bell-older">
              {loading ? t("bell.loading") : t("bell.older")}
            </button>
          </li>
        )}
      </ul>
    </div>
  );
}
