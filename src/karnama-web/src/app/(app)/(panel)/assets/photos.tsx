"use client";

import { Camera, Loader2 } from "lucide-react";
import { useRef, useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { prepareImage } from "@/components/blog/image-prep";
import { uploadForm } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import { useBlobUrl } from "@/lib/use-blob-url";
import type { Photo } from "./types";

const STAGE = { profile: "Profile", handOver: "HandOver", return: "Return" } as const;

/** Shrinks on the phone (≈150 KB) and uploads one photo of the item or of its condition. */
export function AddPhoto({ assetId, stage, assignmentId, onAdded, testId }: { assetId: string; stage: Photo["stage"]; assignmentId?: string | null; onAdded: () => void; testId?: string }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const pick = async (file: File | undefined) => {
    if (!file) return;
    setBusy(true);
    try {
      let blob: Blob = file;
      try {
        blob = (await prepareImage(file, 1280)).blob;
      } catch {
        /* keep the original */
      }
      const form = new FormData();
      form.append("file", blob, "photo.webp");
      const q = new URLSearchParams({ stage: STAGE[stage] });
      if (assignmentId) q.set("assignmentId", assignmentId);
      await uploadForm(`/api/v1/assets/${assetId}/photos?${q}`, form);
      notify.success(t("as.photoAdded"));
      onAdded();
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
      if (input.current) input.current.value = "";
    }
  };
  return (
    <>
      <input ref={input} type="file" accept="image/jpeg,image/png,image/webp" capture="environment" className="sr-only" tabIndex={-1} aria-hidden="true"
        onChange={(e) => pick(e.target.files?.[0])} data-testid={testId ? `${testId}-input` : undefined} />
      <button type="button" disabled={busy} onClick={() => input.current?.click()} className="grid size-20 shrink-0 place-items-center rounded-lg border-2 border-dashed text-xs text-muted-foreground hover:border-foreground"
        data-testid={testId}>
        {busy ? <Loader2 className="size-5 animate-spin" aria-hidden="true" /> : <span className="grid place-items-center gap-1"><Camera className="size-5" aria-hidden="true" />{t("as.addPhoto")}</span>}
      </button>
    </>
  );
}

export function Thumb({ photo }: { photo: Photo }) {
  const { t } = useI18n();
  const url = useBlobUrl(`/api/v1/assets/photos/${photo.id}`, true);
  const [big, setBig] = useState(false);
  if (!url) return <div className="size-20 shrink-0 animate-pulse rounded-lg bg-muted" />;
  return (
    <>
      <button type="button" onClick={() => setBig(true)} className="size-20 shrink-0 overflow-hidden rounded-lg border">
        {/* eslint-disable-next-line @next/next/no-img-element -- private blob */}
        <img src={url} alt="" className="size-full object-cover" />
      </button>
      {big && (
        <button type="button" className="fixed inset-0 z-[60] grid place-items-center bg-black/80 p-4" onClick={() => setBig(false)} aria-label={t("common.close")}>
          {/* eslint-disable-next-line @next/next/no-img-element -- private blob */}
          <img src={url} alt="" className="max-h-full max-w-full rounded-lg" />
        </button>
      )}
    </>
  );
}
