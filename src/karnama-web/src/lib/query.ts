"use client";

/**
 * A small data-fetching cache with the subset of the TanStack Query API this app uses (useQuery, setQueryData,
 * invalidateQueries by key prefix, refetchInterval, staleTime, gcTime). It replaces the 13 KB library so every page
 * stays inside the mobile bandwidth budget (docs/SKILLS.md §7.2). Keys are arrays; a key prefix matches like in
 * TanStack Query.
 */
import { createContext, createElement, useCallback, useContext, useEffect, useRef, useSyncExternalStore } from "react";

type Key = readonly unknown[];
type Fn<T> = (ctx: { signal: AbortSignal }) => Promise<T>;
type Status = "pending" | "success" | "error";

type Entry = {
  key: Key;
  hash: string;
  data: unknown;
  error: unknown;
  status: Status;
  updatedAt: number;
  promise: Promise<unknown> | null;
  fn: Fn<unknown> | null;
  observers: number;
  version: number;
  listeners: Set<() => void>;
  gcTimer?: ReturnType<typeof setTimeout>;
  stale: boolean;
};

export type QueryClientConfig = { defaultOptions?: { queries?: { staleTime?: number; retry?: (count: number, error: unknown) => boolean; refetchOnWindowFocus?: boolean } } };

const hashOf = (key: Key) => JSON.stringify(key);
const startsWith = (key: Key, prefix: Key) => prefix.length <= key.length && prefix.every((p, i) => hashOf([p]) === hashOf([key[i]]));

export class QueryClient {
  private cache = new Map<string, Entry>();
  readonly staleTime: number;
  private readonly retry: (count: number, error: unknown) => boolean;

  constructor(config: QueryClientConfig = {}) {
    this.staleTime = config.defaultOptions?.queries?.staleTime ?? 0;
    this.retry = config.defaultOptions?.queries?.retry ?? (() => false);
  }

  entry(key: Key): Entry {
    const hash = hashOf(key);
    let e = this.cache.get(hash);
    if (!e) {
      e = { key, hash, data: undefined, error: null, status: "pending", updatedAt: 0, promise: null, fn: null, observers: 0, version: 0, listeners: new Set(), stale: false };
      this.cache.set(hash, e);
    }
    return e;
  }

  private notify(e: Entry) {
    e.version++;
    e.listeners.forEach((l) => l());
  }

  fetch(e: Entry): Promise<unknown> {
    if (e.promise) return e.promise;
    if (!e.fn) return Promise.resolve(e.data);
    const fn = e.fn;
    const controller = new AbortController();
    const run = async (attempt: number): Promise<unknown> => {
      try {
        return await fn({ signal: controller.signal });
      } catch (err) {
        if (this.retry(attempt, err)) return run(attempt + 1);
        throw err;
      }
    };
    e.promise = run(0)
      .then((data) => {
        e.data = data;
        e.error = null;
        e.status = "success";
        e.updatedAt = Date.now();
        e.stale = false;
        return data;
      })
      .catch((err) => {
        e.error = err;
        if (e.status !== "success") e.status = "error";
      })
      .finally(() => {
        e.promise = null;
        this.notify(e);
      });
    this.notify(e);
    return e.promise;
  }

  getQueryData<T>(key: Key): T | undefined {
    return this.cache.get(hashOf(key))?.data as T | undefined;
  }

  setQueryData<T>(key: Key, value: T | ((old: T | undefined) => T | undefined)) {
    const e = this.entry(key);
    e.data = typeof value === "function" ? (value as (old: T | undefined) => T | undefined)(e.data as T | undefined) : value;
    e.status = "success";
    e.error = null;
    e.updatedAt = Date.now();
    e.stale = false;
    this.notify(e);
  }

  /** Marks matching queries stale and refetches the ones on screen. */
  invalidateQueries({ queryKey }: { queryKey: Key }): Promise<void> {
    const work: Promise<unknown>[] = [];
    for (const e of this.cache.values()) {
      if (!startsWith(e.key, queryKey)) continue;
      e.stale = true;
      if (e.observers > 0 && e.fn) work.push(this.fetch(e));
    }
    return Promise.all(work).then(() => undefined);
  }

  clear() {
    this.cache.clear();
  }

  observe(e: Entry, gcTime: number) {
    e.observers++;
    clearTimeout(e.gcTimer);
    return () => {
      e.observers--;
      if (e.observers === 0) e.gcTimer = setTimeout(() => e.observers === 0 && this.cache.delete(e.hash), gcTime);
    };
  }
}

const Ctx = createContext<QueryClient | null>(null);

export function QueryClientProvider({ client, children }: { client: QueryClient; children: React.ReactNode }) {
  return createElement(Ctx.Provider, { value: client }, children);
}

export function useQueryClient() {
  const c = useContext(Ctx);
  if (!c) throw new Error("QueryClientProvider missing");
  return c;
}

export type UseQueryOptions<T> = { queryKey: Key; queryFn: Fn<T>; enabled?: boolean; staleTime?: number; gcTime?: number; refetchInterval?: number };
export type UseQueryResult<T> = { data: T | undefined; error: unknown; status: Status; isLoading: boolean; isError: boolean; dataUpdatedAt: number; refetch: () => Promise<unknown> };

export function useQuery<T>({ queryKey, queryFn, enabled = true, staleTime, gcTime = 5 * 60_000, refetchInterval }: UseQueryOptions<T>): UseQueryResult<T> {
  const client = useQueryClient();
  const hash = hashOf(queryKey);
  const entry = client.entry(queryKey);
  // The latest function is used for the next fetch (it may close over new props).
  entry.fn = queryFn as Fn<unknown>;
  const fresh = staleTime ?? client.staleTime;

  const subscribe = useCallback((cb: () => void) => {
    entry.listeners.add(cb);
    return () => void entry.listeners.delete(cb);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the entry is identified by the key hash
  }, [hash]);
  const version = useSyncExternalStore(subscribe, () => entry.version, () => 0);

  useEffect(() => client.observe(entry, gcTime), [client, entry, gcTime]);

  useEffect(() => {
    if (!enabled) return;
    const old = entry.status !== "success" || entry.stale || Date.now() - entry.updatedAt > fresh;
    if (old && !entry.promise) void client.fetch(entry);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- refetch when the key or "enabled" changes
  }, [hash, enabled]);

  const intervalRef = useRef(refetchInterval);
  useEffect(() => {
    intervalRef.current = refetchInterval;
    if (!enabled || !refetchInterval) return;
    const id = setInterval(() => !document.hidden && void client.fetch(entry), refetchInterval);
    return () => clearInterval(id);
  }, [client, entry, enabled, refetchInterval]);

  void version;
  return {
    data: entry.data as T | undefined,
    error: entry.status === "error" || (entry.error && !entry.promise) ? entry.error : null,
    status: entry.status,
    isLoading: enabled && entry.status === "pending",
    isError: entry.status === "error",
    dataUpdatedAt: entry.updatedAt,
    refetch: () => client.fetch(entry),
  };
}
