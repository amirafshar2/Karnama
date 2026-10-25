"use client";

import { Loader2, Undo2, Upload, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { useI18n } from "@/lib/i18n/provider";

type Rect = { x: number; y: number; w: number; h: number };

function pixelate(ctx: CanvasRenderingContext2D, r: Rect) {
  const x = Math.round(Math.min(r.x, r.x + r.w)), y = Math.round(Math.min(r.y, r.y + r.h));
  const w = Math.max(1, Math.round(Math.abs(r.w))), h = Math.max(1, Math.round(Math.abs(r.h)));
  const block = Math.max(8, Math.round(Math.min(w, h) / 6));
  const small = document.createElement("canvas");
  small.width = Math.max(1, Math.round(w / block));
  small.height = Math.max(1, Math.round(h / block));
  small.getContext("2d")!.drawImage(ctx.canvas, x, y, w, h, 0, 0, small.width, small.height);
  ctx.imageSmoothingEnabled = false;
  ctx.drawImage(small, 0, 0, small.width, small.height, x, y, w, h);
  ctx.imageSmoothingEnabled = true;
}

function paint(c: HTMLCanvasElement | null, img: ImageBitmap | null, list: Rect[], d: Rect | null) {
  if (!c || !img) return;
  const ctx = c.getContext("2d")!;
  ctx.drawImage(img, 0, 0, c.width, c.height);
  for (const r of list) pixelate(ctx, r);
  if (d) {
    ctx.strokeStyle = "#feff00";
    ctx.lineWidth = 3;
    ctx.strokeRect(d.x, d.y, d.w, d.h);
  }
}

/**
 * Screenshot privacy tool (docs/PROMPT2.md §12.2): drag over names and numbers to pixelate them, then upload.
 * Everything happens on this device; the original never leaves it.
 */
export function BlurTool({ file, onCancel, onDone }: { file: File; onCancel: () => void; onDone: (blob: Blob) => Promise<void> }) {
  const { t } = useI18n();
  const canvas = useRef<HTMLCanvasElement>(null);
  const img = useRef<ImageBitmap | null>(null);
  const [rects, setRects] = useState<Rect[]>([]);
  const [draft, setDraft] = useState<Rect | null>(null);
  const [busy, setBusy] = useState(false);
  const start = useRef<{ x: number; y: number } | null>(null);

  useEffect(() => {
    let alive = true;
    void createImageBitmap(file).then((b) => {
      if (!alive) return;
      const scale = Math.min(1, 1600 / b.width);
      const c = canvas.current!;
      c.width = Math.round(b.width * scale);
      c.height = Math.round(b.height * scale);
      img.current = b;
      paint(c, b, [], null);
    });
    return () => {
      alive = false;
    };
  }, [file]);

  const draw = (list: Rect[], d: Rect | null) => paint(canvas.current, img.current, list, d);
  const pos = (e: React.PointerEvent) => {
    const c = canvas.current!;
    const b = c.getBoundingClientRect();
    return { x: ((e.clientX - b.left) / b.width) * c.width, y: ((e.clientY - b.top) / b.height) * c.height };
  };

  const upload = async () => {
    setBusy(true);
    draw(rects, null);
    const blob = await new Promise<Blob | null>((r) => canvas.current!.toBlob(r, "image/webp", 0.82));
    try {
      if (blob) await onDone(blob);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-3 rounded-xl border-2 border-foreground p-3" data-testid="blur-tool">
      <p className="font-bold">{t("cases.blurTitle")}</p>
      <p className="text-sm text-muted-foreground">{t("cases.blurHelp")}</p>
      <canvas
        ref={canvas}
        className="w-full touch-none cursor-crosshair rounded-md bg-muted"
        onPointerDown={(e) => {
          e.currentTarget.setPointerCapture(e.pointerId);
          start.current = pos(e);
        }}
        onPointerMove={(e) => {
          if (!start.current) return;
          const p = pos(e);
          const d = { x: start.current.x, y: start.current.y, w: p.x - start.current.x, h: p.y - start.current.y };
          setDraft(d);
          draw(rects, d);
        }}
        onPointerUp={() => {
          if (draft && Math.abs(draft.w) > 4 && Math.abs(draft.h) > 4) {
            const next = [...rects, draft];
            setRects(next);
            draw(next, null);
          }
          start.current = null;
          setDraft(null);
        }}
        data-testid="blur-canvas"
      />
      <div className="flex flex-wrap gap-2">
        <Button type="button" className="h-11" onClick={() => void upload()} disabled={busy} data-testid="blur-upload">
          {busy ? <Loader2 className="animate-spin" aria-hidden="true" /> : <Upload aria-hidden="true" />}
          {t("cases.blurUpload")}
        </Button>
        <Button
          type="button"
          variant="outline"
          className="h-11"
          disabled={rects.length === 0}
          onClick={() => {
            const next = rects.slice(0, -1);
            setRects(next);
            draw(next, null);
          }}
        >
          <Undo2 aria-hidden="true" />
          {t("cases.blurUndo")}
        </Button>
        <Button type="button" variant="ghost" className="h-11" onClick={onCancel} aria-label={t("common.cancel")}>
          <X aria-hidden="true" />
        </Button>
      </div>
    </div>
  );
}
