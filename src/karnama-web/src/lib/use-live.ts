"use client";

import { useQueryClient } from "@/lib/query";
import { useEffect, useRef } from "react";
import { api } from "./api";
import { onChange, type Change } from "./realtime";

/**
 * Keeps the listed queries fresh: a live delta of a watched kind refetches them (batched), and after a reconnect the
 * page asks the server what changed since its last cursor (docs/PROMPT.md §8) — a full refetch only if it fell too far behind.
 */
export function useLive(kinds: string[], queryKeys: unknown[][], onItem?: (c: Change) => void) {
  const qc = useQueryClient();
  const cursor = useRef<number | null>(null);
  const timer = useRef<number | undefined>(undefined);
  const kindsKey = kinds.join(",");
  const keysKey = JSON.stringify(queryKeys);

  useEffect(() => {
    const watched = new Set(kindsKey.split(","));
    const keys = JSON.parse(keysKey) as unknown[][];
    const refresh = () => {
      window.clearTimeout(timer.current);
      timer.current = window.setTimeout(() => keys.forEach((k) => void qc.invalidateQueries({ queryKey: k })), 250);
    };
    void api<{ cursor: number }>("/api/v1/changes").then((r) => (cursor.current = r.cursor)).catch(() => undefined);
    const off = onChange(async (c) => {
      if (c === "resync") {
        if (cursor.current === null) return refresh();
        try {
          const r = await api<{ cursor: number; reset: boolean; events: Change[] }>(`/api/v1/changes?since=${cursor.current}`);
          cursor.current = r.cursor;
          if (r.reset || r.events.some((e) => watched.has(e.kind))) refresh();
        } catch {
          refresh();
        }
        return;
      }
      if (cursor.current === null || c.seq > cursor.current) cursor.current = c.seq;
      if (!watched.has(c.kind)) return;
      onItem?.(c);
      refresh();
    });
    return () => {
      off();
      window.clearTimeout(timer.current);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- onItem is a callback; the subscription follows kinds/keys only
  }, [qc, kindsKey, keysKey]);
}
