"use client";

import { HUB_BASE } from "./config";
import { tokenStore } from "./token-store";

type Handlers = { permissionsChanged: () => void | Promise<void> };

/** A live delta from the server: what kind of thing changed and which fields — the page refetches what it shows. */
export type Change = { seq: number; kind: string; id: string; fields: string[] };
type Listener = (c: Change | "resync") => void;
const listeners = new Set<Listener>();

/** Pages subscribe to live changes; "resync" means the connection came back and the page should catch up. */
export function onChange(fn: Listener) {
  listeners.add(fn);
  return () => void listeners.delete(fn);
}
const emit = (c: Change | "resync") => listeners.forEach((l) => l(c));

/** "Typing…" in a chat (live only, never stored). */
export type Typing = { conversationId: string; userId: string };
const typingListeners = new Set<(t: Typing) => void>();
export function onTyping(fn: (t: Typing) => void) {
  typingListeners.add(fn);
  return () => void typingListeners.delete(fn);
}
let invoke: ((method: string, ...args: unknown[]) => Promise<unknown>) | null = null;
/** Tells the other members of a chat that I'm typing (best effort; ignored when offline). */
export function sendTyping(conversationId: string) {
  void invoke?.("Typing", conversationId).catch(() => undefined);
}

// MessagePack keeps the server's PascalCase names.
type Wire = { Seq?: number; Kind?: string; Id?: string; Fields?: string[]; seq?: number; kind?: string; id?: string; fields?: string[] };

/**
 * Lazily loads SignalR (keeps it out of the first-page bundle), uses MessagePack,
 * reconnects with backoff + jitter, and disconnects while the page is hidden to save data and battery.
 */
export async function connectRealtime(handlers: Handlers) {
  const [{ HubConnectionBuilder, LogLevel, HubConnectionState }, { MessagePackHubProtocol }] = await Promise.all([
    import("@microsoft/signalr"),
    import("@microsoft/signalr-protocol-msgpack"),
  ]);

  const connection = new HubConnectionBuilder()
    .withUrl(`${HUB_BASE}/hubs/notifications`, { accessTokenFactory: () => tokenStore.access ?? "" })
    .withHubProtocol(new MessagePackHubProtocol())
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (ctx) => Math.min(30_000, 1000 * 2 ** ctx.previousRetryCount) + Math.random() * 1000,
    })
    .configureLogging(LogLevel.Warning)
    .build();

  // Roles changed: refresh the token, then reconnect so the server re-decides which live groups this user may join.
  connection.on("PermissionsChanged", async () => {
    await handlers.permissionsChanged();
    await connection.stop();
    await start();
  });
  connection.on("Changed", (m: Wire) => emit({ seq: Number(m.Seq ?? m.seq ?? 0), kind: String(m.Kind ?? m.kind ?? ""), id: String(m.Id ?? m.id ?? ""), fields: m.Fields ?? m.fields ?? [] }));
  connection.on("Typing", (m: { ConversationId?: string; UserId?: string; conversationId?: string; userId?: string }) => {
    const t = { conversationId: String(m.ConversationId ?? m.conversationId ?? ""), userId: String(m.UserId ?? m.userId ?? "") };
    typingListeners.forEach((l) => l(t));
  });
  invoke = (method, ...args) => (connection.state === HubConnectionState.Connected ? connection.invoke(method, ...args) : Promise.resolve());
  connection.onreconnected(() => emit("resync"));
  let started = false;

  const start = async () => {
    if (connection.state !== HubConnectionState.Disconnected || document.hidden) return;
    try {
      await connection.start();
      if (started) emit("resync");  // back from background or a dropped network: catch up
      started = true;
    } catch {
      setTimeout(start, 5000 + Math.random() * 2000);
    }
  };
  const onVisibility = () => (document.hidden ? connection.stop() : start());
  document.addEventListener("visibilitychange", onVisibility);
  await start();

  return () => {
    document.removeEventListener("visibilitychange", onVisibility);
    void connection.stop();
  };
}
