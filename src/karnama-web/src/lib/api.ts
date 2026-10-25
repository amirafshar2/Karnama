import { API_BASE, CLIENT_KIND } from "./config";
import { tokenStore } from "./token-store";

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public traceId?: string,
    public fieldErrors?: Record<string, string[]>,
  ) {
    super(message);
  }
  get isNetwork() {
    return this.status === 0;
  }
}

type Options = Omit<RequestInit, "body"> & {
  body?: unknown;
  /** Skip the automatic refresh-and-retry on 401 (used by auth endpoints themselves). */
  noAuthRetry?: boolean;
  timeoutMs?: number;
};

const TIMEOUT = 15_000;
let refreshing: Promise<boolean> | null = null;
let onSessionLost: (() => void) | null = null;

export function setSessionLostHandler(handler: () => void) {
  onSessionLost = handler;
}

function baseHeaders(): Record<string, string> {
  const h: Record<string, string> = { Accept: "application/json" };
  if (typeof document !== "undefined") h["Accept-Language"] = document.documentElement.lang || "fa";
  if (CLIENT_KIND === "android") h["X-Client"] = "android";
  return h;
}

async function rawFetch(path: string, opts: Options): Promise<Response> {
  const headers: Record<string, string> = { ...baseHeaders(), ...(opts.headers as Record<string, string>) };
  if (opts.body !== undefined) headers["Content-Type"] = "application/json";
  if (tokenStore.access) headers.Authorization = `Bearer ${tokenStore.access}`;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), opts.timeoutMs ?? TIMEOUT);
  const external = opts.signal;
  external?.addEventListener("abort", () => controller.abort());
  try {
    return await fetch(API_BASE + path, {
      ...opts,
      headers,
      body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
      credentials: "include",
      signal: controller.signal,
    });
  } finally {
    clearTimeout(timer);
  }
}

async function toError(res: Response): Promise<ApiError> {
  let body: Record<string, unknown> = {};
  try {
    body = await res.json();
  } catch {
    /* not JSON */
  }
  return new ApiError(
    res.status,
    (body.code as string) ?? `http.${res.status}`,
    (body.title as string) ?? "",
    body.traceId as string | undefined,
    body.errors as Record<string, string[]> | undefined,
  );
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/**
 * JSON fetch with timeout, retry with backoff + jitter for safe (GET) requests,
 * and one transparent token refresh on 401.
 */
export async function api<T>(path: string, opts: Options = {}): Promise<T> {
  const method = (opts.method ?? "GET").toUpperCase();
  const attempts = method === "GET" ? 3 : 1;
  let lastError: ApiError | null = null;

  for (let i = 0; i < attempts; i++) {
    let res: Response;
    try {
      res = await rawFetch(path, opts);
    } catch {
      lastError = new ApiError(0, "network", "");
      if (i < attempts - 1) await sleep(400 * 2 ** i + Math.random() * 300);
      continue;
    }

    if (res.status === 401 && !opts.noAuthRetry) {
      if (await refreshSession()) {
        res = await rawFetch(path, opts);
      } else {
        onSessionLost?.();
      }
    }
    if (res.ok) {
      if (res.status === 204) return undefined as T;
      return (await res.json()) as T;
    }
    lastError = await toError(res);
    if (res.status >= 500 && i < attempts - 1) {
      await sleep(400 * 2 ** i + Math.random() * 300);
      continue;
    }
    throw lastError;
  }
  throw lastError ?? new ApiError(0, "network", "");
}

export type Session = { accessToken: string; expiresAt: string; refreshToken?: string };

export async function storeSession(s: Session) {
  tokenStore.setAccess(s.accessToken);
  if (s.refreshToken) await tokenStore.setRefresh(s.refreshToken);
}

/** Single-flight refresh, also across browser tabs (Web Locks), so rotation never looks like token reuse. */
export function refreshSession(): Promise<boolean> {
  if (refreshing) return refreshing;
  const run = async () => {
    try {
      const refresh = await tokenStore.getRefresh();
      const res = await rawFetch("/api/v1/auth/refresh", {
        method: "POST",
        headers: refresh ? { "X-Refresh-Token": refresh } : undefined,
      });
      if (!res.ok) {
        tokenStore.setAccess(null);
        return false;
      }
      await storeSession((await res.json()) as Session);
      return true;
    } catch {
      return false;
    }
  };
  const locks = typeof navigator !== "undefined" ? (navigator as Navigator & { locks?: LockManager }).locks : undefined;
  const promise: Promise<boolean> = (locks ? (locks.request("karnama-refresh", run) as unknown as Promise<boolean>) : run()).finally(() => {
    refreshing = null;
  });
  refreshing = promise;
  return promise;
}
