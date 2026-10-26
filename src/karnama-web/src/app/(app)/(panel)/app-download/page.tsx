"use client";

import { Download, ShieldCheck, Smartphone } from "lucide-react";
import { useEffect, useState } from "react";
import { APK_URL } from "@/lib/config";
import { faAppDl } from "@/lib/i18n/fa-appdl";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";

registerTexts(faAppDl);

/** Download page for the Android app (every signed-in person, website only). The APK comes from APK_URL (a GitHub release). */
export default function AppDownloadPage() {
  const { t } = useI18n();
  const [qr, setQr] = useState<string | null>(null);
  useEffect(() => {
    if (!APK_URL) return;
    void import("qrcode").then(({ toDataURL }) => toDataURL(APK_URL, { margin: 1, width: 360, errorCorrectionLevel: "M" }).then(setQr));
  }, []);

  const steps = ["appdl.step1", "appdl.step2", "appdl.step3", "appdl.step4"] as const;
  return (
    <div className="mx-auto max-w-3xl space-y-6">
      <header>
        <h1 className="text-2xl font-extrabold">{t("appdl.title")}</h1>
        <p className="mt-1 max-w-prose text-muted-foreground">{t("appdl.subtitle")}</p>
      </header>

      <section className="grid gap-6 rounded-2xl border bg-card p-5 sm:grid-cols-[1fr_auto] sm:items-center" data-testid="appdl-card">
        <div className="space-y-3">
          {APK_URL ? (
            <>
              <a
                href={APK_URL}
                download="karnama.apk"
                rel="noopener"
                className="inline-flex min-h-14 w-full items-center justify-center gap-2 rounded-xl bg-primary px-6 text-lg font-bold text-primary-foreground shadow-sm hover:opacity-90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring sm:w-auto"
                data-testid="appdl-download"
              >
                <Download className="size-6" aria-hidden="true" />
                {t("appdl.download")}
              </a>
              <p className="text-sm text-muted-foreground">{t("appdl.version")}</p>
            </>
          ) : (
            <p className="rounded-lg bg-muted p-4 text-sm" data-testid="appdl-not-ready">{t("appdl.notReady")}</p>
          )}
        </div>
        {qr && (
          <figure className="hidden flex-col items-center gap-2 text-center sm:flex">
            {/* eslint-disable-next-line @next/next/no-img-element -- generated data URL */}
            <img src={qr} alt="" className="size-40 rounded-lg border bg-white p-1" />
            <figcaption className="max-w-48 text-xs text-muted-foreground">{t("appdl.qr")}</figcaption>
          </figure>
        )}
      </section>

      <section className="space-y-3">
        <h2 className="text-lg font-bold">{t("appdl.stepsTitle")}</h2>
        <ol className="space-y-3">
          {steps.map((k, i) => (
            <li key={k} className="flex gap-3 rounded-xl border bg-card p-4">
              <span className="grid size-8 shrink-0 place-items-center rounded-full bg-primary text-sm font-bold text-primary-foreground" aria-hidden="true">
                {(i + 1).toLocaleString("fa-IR")}
              </span>
              <span className="leading-7">{t(k)}</span>
            </li>
          ))}
        </ol>
      </section>

      <section className="grid gap-4 sm:grid-cols-2">
        <div className="rounded-xl border bg-card p-4">
          <h2 className="flex items-center gap-2 font-bold">
            <Smartphone className="size-5" aria-hidden="true" />
            {t("appdl.whyTitle")}
          </h2>
          <p className="mt-2 text-sm leading-7 text-muted-foreground">{t("appdl.why")}</p>
        </div>
        <div className="rounded-xl border bg-card p-4">
          <h2 className="flex items-center gap-2 font-bold">
            <ShieldCheck className="size-5" aria-hidden="true" />
            {t("appdl.safeTitle")}
          </h2>
          <p className="mt-2 text-sm leading-7 text-muted-foreground">{t("appdl.safe")}</p>
        </div>
      </section>

      <p className="text-sm text-muted-foreground">{t("appdl.update")}</p>
      <p className="text-sm text-muted-foreground">{t("appdl.iphone")}</p>
    </div>
  );
}
