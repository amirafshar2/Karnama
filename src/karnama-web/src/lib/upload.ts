"use client";

import { api, refreshSession } from "./api";
import { API_BASE } from "./config";
import { tokenStore } from "./token-store";

export type UploadTarget = { orderId: string; workItemId?: string | null; purpose: "brief" | "deliverable" | "attachment" };
type Status = { id: string; size: number; received: number; complete: boolean; chunkSize: number };

const RESUME_KEY = "karnama.uploads";
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

function remembered(): Record<string, string> {
  try {
    return JSON.parse(localStorage.getItem(RESUME_KEY) ?? "{}");
  } catch {
    return {};
  }
}
function remember(key: string, id: string | null) {
  try {
    const m = remembered();
    if (id) m[key] = id;
    else delete m[key];
    localStorage.setItem(RESUME_KEY, JSON.stringify(m));
  } catch {
    /* private mode: resume still works within this page */
  }
}

async function patch(id: string, offset: number, chunk: Blob, signal?: AbortSignal) {
  const send = () =>
    fetch(`${API_BASE}/api/v1/files/${id}/upload`, {
      method: "PATCH",
      body: chunk,
      credentials: "include",
      signal,
      headers: { "Upload-Offset": String(offset), ...(tokenStore.access ? { Authorization: `Bearer ${tokenStore.access}` } : {}) },
    });
  let res = await send();
  if (res.status === 401 && (await refreshSession())) res = await send();
  return res;
}

/**
 * Resumable upload in chunks. A dropped connection (common on mobile data) retries with backoff and continues from
 * the byte the server has; reopening the page continues the same upload instead of starting over.
 */
export async function uploadFile(file: File, target: UploadTarget, onProgress: (sent: number, total: number) => void, signal?: AbortSignal) {
  const key = `${target.orderId}:${target.workItemId ?? ""}:${file.name}:${file.size}:${file.lastModified}`;
  let status: Status | null = null;
  const known = remembered()[key];
  if (known) status = await api<Status>(`/api/v1/files/${known}/upload`).catch(() => null);
  if (!status || status.complete) {
    status = await api<Status>("/api/v1/files", {
      method: "POST",
      body: { ...target, fileName: file.name, contentType: file.type || "application/octet-stream", size: file.size },
    });
    remember(key, status.id);
  }

  let offset = status.received;
  let failures = 0;
  onProgress(offset, file.size);
  while (offset < file.size) {
    if (signal?.aborted) throw new DOMException("aborted", "AbortError");
    const end = Math.min(file.size, offset + status.chunkSize);
    try {
      const res = await patch(status.id, offset, file.slice(offset, end), signal);
      if (res.status === 409) {
        // Server has a different offset (e.g. a retry after a lost response): ask and continue from there.
        offset = (await api<Status>(`/api/v1/files/${status.id}/upload`)).received;
        continue;
      }
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        remember(key, null);
        throw Object.assign(new Error(body.title ?? "upload failed"), { status: res.status, code: body.code });
      }
      offset = ((await res.json()) as Status).received;
      failures = 0;
      onProgress(offset, file.size);
    } catch (e) {
      if ((e as { status?: number }).status || signal?.aborted) throw e;
      if (++failures > 8) throw e;
      await sleep(Math.min(30_000, 1000 * 2 ** failures) + Math.random() * 1000);
      offset = (await api<Status>(`/api/v1/files/${status.id}/upload`).catch(() => ({ received: offset }) as Status)).received;
    }
  }
  remember(key, null);
  return status.id;
}

/** Downloads a private file with the session token (links can't carry it). */
export async function downloadFile(id: string, name: string) {
  const get = () => fetch(`${API_BASE}/api/v1/files/${id}`, { credentials: "include", headers: tokenStore.access ? { Authorization: `Bearer ${tokenStore.access}` } : undefined });
  let res = await get();
  if (res.status === 401 && (await refreshSession())) res = await get();
  if (!res.ok) throw new Error("download failed");
  const url = URL.createObjectURL(await res.blob());
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
