"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { BellOff, Briefcase, MessageSquarePlus, Search, Settings2, Users } from "lucide-react";
import dynamic from "next/dynamic";
import { useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { ErrorText } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { faN6 } from "@/lib/i18n/fa-n6";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { hasPermission } from "@/lib/types";
import { useLive } from "@/lib/use-live";
import { cn } from "@/lib/utils";
import type { Conversation, SearchHit } from "./types";

registerTexts(faN6);

const Thread = dynamic(() => import("./thread").then((m) => m.Thread), { ssr: false });
const NewChat = dynamic(() => import("./new-chat").then((m) => m.NewChat), { ssr: false });
const ChatSettings = dynamic(() => import("./new-chat").then((m) => m.ChatSettings), { ssr: false });

const LIST = ["chat"];

/** PROMPT2 §7: conversations on the side, the open one beside it (phones: one at a time). */
export default function ChatPage() {
  const { me } = useAuth();
  const router = useRouter();
  const { t, formatDate, formatNumber } = useI18n();
  const qc = useQueryClient();
  const allowed = !!me && (me.kind === "client" || hasPermission(me, "chat.use", "own") || hasPermission(me, "chat.groups.manage", "all"));
  const isManager = hasPermission(me, "chat.groups.manage", "all");
  const canSettings = hasPermission(me, "settings.manage", "all");
  const list = useQuery({ queryKey: LIST, enabled: allowed, queryFn: () => api<Conversation[]>("/api/v1/chat") });
  useLive(["chat"], [LIST]);
  const [openId, setOpenId] = useState<string | null>(() => (typeof window === "undefined" ? null : new URLSearchParams(window.location.search).get("c")));
  const [creating, setCreating] = useState<"direct" | "group" | "edit" | null>(null);
  const [settings, setSettings] = useState(false);
  const [query, setQuery] = useState("");
  const [orderError, setOrderError] = useState<unknown>(null);
  const search = useQuery({
    queryKey: ["chat-search", query],
    enabled: allowed && query.trim().length >= 2,
    queryFn: () => api<SearchHit[]>(`/api/v1/chat/search?q=${encodeURIComponent(query.trim())}`),
  });

  useEffect(() => {
    if (me && !allowed) router.replace("/dashboard");
  }, [me, allowed, router]);

  // "Chat about this work" from an order: /chat?order=<id> opens (or creates) that order's chat.
  useEffect(() => {
    const order = new URLSearchParams(window.location.search).get("order");
    if (!order || !allowed) return;
    api<Conversation>(`/api/v1/chat/orders/${order}`, { method: "POST" })
      .then(async (c) => {
        await qc.invalidateQueries({ queryKey: LIST });
        setOpenId(c.id);
      })
      .catch(setOrderError);
  }, [allowed, qc]);

  if (!allowed) return null;
  const open = list.data?.find((c) => c.id === openId) ?? null;
  const time = (iso: string) => formatDate(iso, { hour: "2-digit", minute: "2-digit" });

  return (
    <div className="-mx-4 -mt-6 flex h-[calc(100dvh-11.5rem)] min-h-[26rem] overflow-hidden border-y bg-card md:mx-0 md:mt-0 md:h-[calc(100dvh-9rem)] md:rounded-xl md:border" data-testid="chat">
      <aside className={cn("flex w-full flex-col border-e md:w-80 md:shrink-0", open && "hidden md:flex")}>
        <div className="space-y-3 border-b p-3">
          <div className="flex items-center justify-between gap-2">
            <h1 className="text-xl font-extrabold">{t("chat.title")}</h1>
            <div className="flex gap-1">
              {canSettings && (
                <Button variant="ghost" size="icon" className="size-11" aria-label={t("chat.settings")} onClick={() => setSettings(true)} data-testid="chat-settings">
                  <Settings2 aria-hidden="true" />
                </Button>
              )}
              {isManager && (
                <Button variant="ghost" size="icon" className="size-11" aria-label={t("chat.newGroup")} onClick={() => setCreating("group")} data-testid="chat-new-group">
                  <Users aria-hidden="true" />
                </Button>
              )}
              <Button size="icon" className="size-11" aria-label={t("chat.new")} onClick={() => setCreating("direct")} data-testid="chat-new">
                <MessageSquarePlus aria-hidden="true" />
              </Button>
            </div>
          </div>
          <label className="relative block">
            <span className="sr-only">{t("chat.search")}</span>
            <Search className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
            <input type="search" value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t("chat.search")}
              className="h-11 w-full rounded-lg border bg-background ps-9 pe-3 text-sm" data-testid="chat-search" />
          </label>
        </div>
        <ErrorText error={list.error ?? orderError} />
        <div className="min-h-0 flex-1 overflow-y-auto">
          {query.trim().length >= 2 ? (
            <ul data-testid="chat-search-results">
              {search.data?.length === 0 && <li className="p-4 text-sm text-muted-foreground">{t("chat.searchNone")}</li>}
              {search.data?.map((h) => (
                <li key={h.id}>
                  <button className="w-full border-b p-3 text-start hover:bg-muted" onClick={() => { setOpenId(h.conversationId); setQuery(""); }}>
                    <span className="block text-sm font-semibold">{h.conversationTitle}</span>
                    <span className="block text-xs text-muted-foreground">{h.authorName}: {h.snippet}</span>
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <ul data-testid="chat-list">
              {list.data?.length === 0 && <li className="p-6 text-center text-sm text-muted-foreground">{t("chat.empty")}</li>}
              {list.data?.map((c) => (
                <li key={c.id}>
                  <button onClick={() => setOpenId(c.id)} aria-current={c.id === openId ? "true" : undefined}
                    className={cn("flex w-full items-center gap-3 border-b p-3 text-start hover:bg-muted", c.id === openId && "bg-muted")} data-testid={`chat-item-${c.title}`}>
                    <span className="flex size-11 shrink-0 items-center justify-center rounded-full bg-secondary text-secondary-foreground" aria-hidden="true">
                      {c.kind === "group" ? <Users className="size-5" /> : c.kind === "work" ? <Briefcase className="size-5" /> : <span className="text-base font-bold">{c.title.slice(0, 1)}</span>}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="flex items-center justify-between gap-2">
                        <span className="truncate font-semibold">{c.title}</span>
                        <span className="shrink-0 text-xs text-muted-foreground">{c.lastPreview ? time(c.lastMessageAt) : ""}</span>
                      </span>
                      <span className="flex items-center justify-between gap-2">
                        <span className="truncate text-sm text-muted-foreground">
                          {c.otherIsClient && <span className="me-1 rounded bg-amber-100 px-1 text-xs text-amber-950 dark:bg-amber-950 dark:text-amber-100">{t("chat.client")}</span>}
                          {c.lastPreview ?? (c.kind === "group" ? t("chat.kind.group") : c.kind === "work" ? t("chat.kind.work") : "")}
                        </span>
                        {c.muted && <BellOff className="size-3.5 shrink-0 text-muted-foreground" aria-label={t("chat.mute")} />}
                        {c.unread > 0 && <span className="min-w-6 shrink-0 rounded-full bg-primary px-1.5 text-center text-xs font-bold text-primary-foreground">{formatNumber(c.unread)}</span>}
                      </span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </aside>
      <section className={cn("min-w-0 flex-1", !open && "hidden md:flex md:items-center md:justify-center")}>
        {open ? (
          <Thread key={open.id} conversation={open} isManager={isManager} onBack={() => setOpenId(null)} onEditGroup={() => setCreating("edit")} />
        ) : (
          <p className="p-6 text-muted-foreground">{t("chat.pick")}</p>
        )}
      </section>
      {creating && (
        <NewChat
          mode={creating === "direct" ? "direct" : "group"}
          group={creating === "edit" && open?.kind === "group" ? open : null}
          onClose={() => setCreating(null)}
          onOpened={async (id) => {
            setCreating(null);
            await qc.invalidateQueries({ queryKey: LIST });
            setOpenId(id);
          }}
        />
      )}
      {settings && <ChatSettings onClose={() => setSettings(false)} />}
    </div>
  );
}
