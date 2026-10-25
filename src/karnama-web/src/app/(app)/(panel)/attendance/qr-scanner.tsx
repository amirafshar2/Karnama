"use client";

import { useEffect, useRef, useState } from "react";
import { Input } from "@/components/ui/input";
import { useI18n } from "@/lib/i18n/provider";
import { isNative, nativeScanQr } from "@/lib/native";

type Detector = { detect: (s: CanvasImageSource) => Promise<{ rawValue: string }[]> };

/** Camera QR scan: the built-in BarcodeDetector (Android Chrome) or the small jsQR fallback, loaded only here. */
export function QrScanner({ onCode }: { onCode: (code: string) => void }) {
  const { t } = useI18n();
  const video = useRef<HTMLVideoElement>(null);
  const [manual, setManual] = useState("");
  const [error, setError] = useState(false);

  useEffect(() => {
    let stream: MediaStream | null = null;
    let stopped = false;
    let raf = 0;
    (async () => {
      // Android app: the phone's own scanner first (faster, works in low light); in-page camera if it isn't available.
      if (isNative) {
        const code = await nativeScanQr();
        if (stopped) return;
        if (code) return onCode(code);
      }
      try {
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" }, audio: false });
        if (!video.current || stopped) return;
        video.current.srcObject = stream;
        await video.current.play();
        const BD = (window as unknown as { BarcodeDetector?: new (o: { formats: string[] }) => Detector }).BarcodeDetector;
        const detector = BD ? new BD({ formats: ["qr_code"] }) : null;
        const jsQR = detector ? null : (await import("jsqr")).default;
        const canvas = document.createElement("canvas");
        const ctx = canvas.getContext("2d", { willReadFrequently: true });
        const tick = async () => {
          if (stopped || !video.current) return;
          try {
            if (detector) {
              const found = await detector.detect(video.current);
              if (found[0]?.rawValue) return onCode(found[0].rawValue);
            } else if (jsQR && ctx && video.current.videoWidth) {
              canvas.width = video.current.videoWidth;
              canvas.height = video.current.videoHeight;
              ctx.drawImage(video.current, 0, 0);
              const img = ctx.getImageData(0, 0, canvas.width, canvas.height);
              const r = jsQR(img.data, img.width, img.height);
              if (r?.data) return onCode(r.data);
            }
          } catch {
            /* keep scanning */
          }
          raf = window.setTimeout(tick, 250);
        };
        void tick();
      } catch {
        setError(true);
      }
    })();
    return () => {
      stopped = true;
      window.clearTimeout(raf);
      stream?.getTracks().forEach((tr) => tr.stop());
    };
  }, [onCode]);

  return (
    <div className="space-y-3">
      {!error && <video ref={video} className="aspect-square w-full max-w-xs rounded-xl bg-black object-cover" muted playsInline aria-label={t("att.qrHelp")} />}
      <p className="text-sm text-muted-foreground">{t("att.qrHelp")}</p>
      <label className="block space-y-1 text-sm">
        {t("att.qrManual")}
        <Input dir="ltr" className="h-12" value={manual} onChange={(e) => setManual(e.target.value)} onBlur={() => manual.trim() && onCode(manual.trim())} data-testid="qr-manual" />
      </label>
    </div>
  );
}
