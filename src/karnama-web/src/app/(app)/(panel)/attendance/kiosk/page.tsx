"use client";

import { useQuery } from "@/lib/query";
import { useEffect, useState } from "react";
import { useRequire } from "@/components/app/use-require";
import { api } from "@/lib/api";
import { faM4 } from "@/lib/i18n/fa-m4";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";

registerTexts(faM4);

/** The office tablet: a QR that changes every 30 seconds and right after each use (PROMPT2 §2.1 RotatingQr). */
export default function KioskPage() {
  const allowed = useRequire([{ code: "attendance.kiosk", scope: "all" }]);
  const { t, formatNumber } = useI18n();
  const code = useQuery({ queryKey: ["kiosk"], enabled: allowed, queryFn: () => api<{ code: string; secondsLeft: number }>("/api/v1/attendance/kiosk/code"), refetchInterval: 3000 });
  const [img, setImg] = useState<string | null>(null);
  useEffect(() => {
    const value = code.data?.code;
    if (!value) return;
    void import("qrcode").then(({ toDataURL }) => toDataURL(value, { margin: 1, width: 640, errorCorrectionLevel: "M" }).then(setImg));
  }, [code.data]);
  if (!allowed) return null;
  return (
    <div className="mx-auto flex max-w-xl flex-col items-center gap-4 text-center" data-testid="kiosk">
      <h1 className="text-2xl font-extrabold">{t("kiosk.title")}</h1>
      <p className="text-muted-foreground">{t("kiosk.help")}</p>
      {img ? (
        // eslint-disable-next-line @next/next/no-img-element -- generated data URL
        <img src={img} alt={t("kiosk.title")} className="aspect-square w-full max-w-md rounded-2xl border bg-white p-2" data-testid="kiosk-qr" />
      ) : (
        <div className="aspect-square w-full max-w-md animate-pulse rounded-2xl bg-muted" />
      )}
      {code.data && (
        <>
          <p className="text-sm text-muted-foreground">{t("kiosk.seconds", { s: formatNumber(code.data.secondsLeft) })}</p>
          <code dir="ltr" className="select-all rounded bg-muted px-2 py-1 text-xs" data-testid="kiosk-code">
            {code.data.code}
          </code>
        </>
      )}
    </div>
  );
}
