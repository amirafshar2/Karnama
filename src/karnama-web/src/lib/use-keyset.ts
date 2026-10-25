"use client";

import { useQuery, useQueryClient } from "@/lib/query";
import { useState } from "react";
import { api } from "./api";
import type { KeysetPage } from "./types";

/**
 * Keyset-paginated list for every CRUD table: first page cached by React Query, "load more" appends with the
 * server cursor. (Lighter than useInfiniteQuery, which the bandwidth budget can't afford on every list page.)
 */
export function useKeysetList<T>(baseKey: string, path: string, params: Record<string, string | undefined>, enabled = true) {
  const qc = useQueryClient();
  const search = new URLSearchParams(Object.entries(params).filter((e): e is [string, string] => !!e[1]));
  const key = [baseKey, search.toString()];
  const first = useQuery({
    queryKey: key,
    enabled,
    queryFn: ({ signal }) => api<KeysetPage<T>>(`${path}?${search}`, { signal }),
  });
  const [more, setMore] = useState<{ for: string; at: number; items: T[]; cursor: string | null }>({ for: "", at: 0, items: [], cursor: null });
  const [loadingMore, setLoadingMore] = useState(false);

  // Extra pages belong to one exact first page; a refetch (new data) starts over.
  const valid = more.for === search.toString() && more.at === first.dataUpdatedAt;
  const items = [...(first.data?.items ?? []), ...(valid ? more.items : [])];
  const cursor = valid && more.items.length ? more.cursor : (first.data?.nextCursor ?? null);

  const loadMore = async () => {
    if (!cursor) return;
    setLoadingMore(true);
    try {
      const p = new URLSearchParams(search);
      p.set("cursor", cursor);
      const page = await api<KeysetPage<T>>(`${path}?${p}`);
      setMore({ for: search.toString(), at: first.dataUpdatedAt, items: [...(valid ? more.items : []), ...page.items], cursor: page.nextCursor });
    } finally {
      setLoadingMore(false);
    }
  };

  return {
    items,
    isLoading: first.isLoading,
    error: first.error,
    hasMore: !!cursor,
    loadingMore,
    loadMore,
    refresh: () => qc.invalidateQueries({ queryKey: [baseKey] }),
  };
}
