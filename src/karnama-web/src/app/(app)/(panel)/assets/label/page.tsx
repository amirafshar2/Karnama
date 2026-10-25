"use client";

import { Printer } from "lucide-react";
import { useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { ErrorText } from "@/components/app/error-text";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faN3 } from "@/lib/i18n/fa-n3";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { useQuery } from "@/lib/query";
import type { AssetDetail } from "../types";

registerTexts(faN3);

/** Printable QR label: brand, name and property code. Scanning it opens the asset in the app (or the website). */
function Inner() {
  const allowed = useRequire([{ code: "assets.assign", scope: "all" }, { code: "assets.manage", scope: "all" }]);
  const { t } = useI18n();
  const id = useSearchParams().get("id") ?? "";
  const q = useQuery({ queryKey: ["asset", id], enabled: allowed && /^[0-9a-f-]{36}$/i.test(id), queryFn: () => api<AssetDetail>(`/api/v1/assets/${id}`) });
  const [img, setImg] = useState<string | null>(null);
  const tag = q.data?.asset.tag;
  useEffect(() => {
    if (!tag) return;
    const url = `${window.location.origin}/assets?tag=${encodeURIComponent(tag)}`;
    void import("qrcode").then(({ toDataURL }) => toDataURL(url, { margin: 1, width: 480, errorCorrectionLevel: "M" }).then(setImg));
  }, [tag]);
  if (!allowed) return null;
  if (q.error) return <ErrorText error={q.error} />;
  const a = q.data?.asset;
  return (
    <div className="space-y-5">
      <header data-print-hide>
        <h1 className="text-2xl font-extrabold">{t("as.label")}</h1>
        <p className="mt-1 max-w-prose text-muted-foreground">{t("as.labelHelp")}</p>
      </header>
      {a && (
        <figure className="mx-auto flex w-[62mm] flex-col items-center gap-1 rounded-lg border-2 border-black bg-white p-3 text-center text-black" data-testid="as-label-card">
          {/* eslint-disable-next-line @next/next/no-img-element -- generated data URL */}
          {img ? <img src={img} alt={a.tag} className="size-40" /> : <div className="size-40 animate-pulse bg-neutral-200" />}
          <figcaption>
            <div className="text-sm font-bold leading-tight">{a.name}</div>
            <div className="font-mono text-lg font-extrabold tracking-wider" dir="ltr">{a.tag}</div>
          </figcaption>
        </figure>
      )}
      <div className="flex justify-center" data-print-hide>
        <Button className="h-12" onClick={() => window.print()} disabled={!img} data-testid="as-label-print"><Printer aria-hidden="true" />{t("as.print")}</Button>
      </div>
    </div>
  );
}

export default function AssetLabelPage() {
  return (
    <Suspense>
      <Inner />
    </Suspense>
  );
}
