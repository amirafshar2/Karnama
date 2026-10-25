"use client";

import { HUB_BASE } from "./config";
import { tokenStore } from "./token-store";

type Handlers = { permissionsChanged: () => void };

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

  const start = async () => {
    if (connection.state !== HubConnectionState.Disconnected || document.hidden) return;
    try {
      await connection.start();
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
