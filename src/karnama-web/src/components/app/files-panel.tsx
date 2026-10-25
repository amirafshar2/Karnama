"use client";

import { Download, Eye, EyeOff, FileIcon, Loader2, Upload } from "lucide-react";
import { useRef, useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import { downloadFile, uploadFile, type UploadTarget } from "@/lib/upload";
import type { FileItem } from "@/lib/work-types";

const size = (n: number) => (n > 1024 * 1024 ? `${(n / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(n / 1024))} KB`);

/** File list + resumable upload with a progress bar. */
export function FilesPanel({ files, target, canUpload, canShare, onChanged, testId = "files" }: { files: FileItem[]; target: UploadTarget; canUpload: boolean; canShare: boolean; onChanged: () => void; testId?: string }) {
  const { t, formatNumber } = useI18n();
  const errorMessage = useErrorMessage();
  const input = useRef<HTMLInputElement>(null);
  const [progress, setProgress] = useState<{ name: string; percent: number } | null>(null);

  const upload = async (list: FileList | null) => {
    for (const file of Array.from(list ?? [])) {
      setProgress({ name: file.name, percent: 0 });
      try {
        await uploadFile(file, target, (sent, total) => setProgress({ name: file.name, percent: Math.floor((sent / total) * 100) }));
        notify.success(t("work.uploaded"));
        onChanged();
      } catch (e) {
        notify.error(errorMessage(e));
      }
    }
    setProgress(null);
    if (input.current) input.current.value = "";
  };

  const share = async (f: FileItem) => {
    try {
      await api(`/api/v1/files/${f.id}/share?visible=${!f.clientVisible}`, { method: "POST" });
      onChanged();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  return (
    <div className="space-y-2" data-testid={testId}>
      <ul className="divide-y rounded-lg border">
        {files.map((f) => (
          <li key={f.id} className="flex flex-wrap items-center gap-2 px-3 py-2 text-sm">
            <FileIcon className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
            <bdi className="min-w-0 flex-1 truncate">{f.fileName}</bdi>
            <span className="text-xs text-muted-foreground" dir="ltr">
              {size(f.size)}
            </span>
            {canShare && (
              <Button type="button" size="sm" variant="ghost" className="h-10" onClick={() => share(f)} aria-pressed={f.clientVisible}>
                {f.clientVisible ? <Eye className="size-4" aria-hidden="true" /> : <EyeOff className="size-4" aria-hidden="true" />}
                {f.clientVisible ? t("work.shared") : t("work.share")}
              </Button>
            )}
            <Button type="button" size="sm" variant="outline" className="h-10" onClick={() => downloadFile(f.id, f.fileName).catch(() => notify.error(t("common.errorGeneric")))}>
              <Download className="size-4" aria-hidden="true" />
              {t("work.download")}
            </Button>
          </li>
        ))}
        {files.length === 0 && <li className="px-3 py-3 text-sm text-muted-foreground">—</li>}
      </ul>
      {progress && (
        <div className="space-y-1" aria-live="polite">
          <p className="flex items-center gap-2 text-sm">
            <Loader2 className="size-4 animate-spin" aria-hidden="true" />
            <bdi className="truncate">{progress.name}</bdi> — {t("work.uploading", { percent: formatNumber(progress.percent) })}
          </p>
          <div className="h-2 overflow-hidden rounded-full bg-muted" role="progressbar" aria-valuenow={progress.percent} aria-valuemin={0} aria-valuemax={100}>
            <div className="h-full bg-primary transition-[width]" style={{ width: `${progress.percent}%` }} />
          </div>
        </div>
      )}
      {canUpload && (
        <>
          <input ref={input} type="file" multiple className="sr-only" onChange={(e) => upload(e.target.files)} data-testid={`${testId}-input`} />
          <Button type="button" variant="outline" className="h-12 md:h-11" disabled={!!progress} onClick={() => input.current?.click()}>
            <Upload className="size-4" aria-hidden="true" />
            {t("work.upload")}
          </Button>
          <p className="text-xs text-muted-foreground">{t("work.uploadHelp")}</p>
        </>
      )}
    </div>
  );
}
