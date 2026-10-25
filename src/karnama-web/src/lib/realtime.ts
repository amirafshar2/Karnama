"use client";

import { HUB_BASE } from "./config";
import { tokenStore } from "./token-store";

type Handlers = { permissionsChanged: () => void };

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

  connection.on("PermissionsChanged", handlers.permissionsChanged);
  connection.on("Changed", (m: Wire) => emit({ seq: Number(m.Seq ?? m.seq ?? 0), kind: String(m.Kind ?? m.kind ?? ""), id: String(m.Id ?? m.id ?? ""), fields: m.Fields ?? m.fields ?? [] }));
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
