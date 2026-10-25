"use client";

import { useQuery } from "@tanstack/react-query";
import { CheckCircle2, Loader2 } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { BrandMark } from "@/components/app/brand-mark";
import { ErrorText } from "@/components/app/error-text";
import { TilePattern } from "@/components/app/tile-pattern";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { api } from "@/lib/api";
import { toLatinDigits, useI18n } from "@/lib/i18n/provider";
import { faAuth } from "@/lib/i18n/fa-pages";
import { registerTexts as registerPageTexts } from "@/lib/i18n/registry";

registerPageTexts(faAuth);

export function SetupScreen() {
  const { t } = useI18n();
  const status = useQuery({ queryKey: ["setup-status"], queryFn: () => api<{ needsSetup: boolean }>("/api/v1/setup/status") });
  const [form, setForm] = useState({ token: "", displayName: "", phone: "", brandName: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [done, setDone] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api("/api/v1/setup", {
        method: "POST",
        body: { ...form, phone: toLatinDigits(form.phone), brandName: form.brandName || null },
        noAuthRetry: true,
      });
      setDone(true);
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const field = (key: keyof typeof form, label: string, props: React.ComponentProps<"input"> = {}) => (
    <div className="space-y-2">
      <Label htmlFor={key}>{label}</Label>
      <Input id={key} className="h-12" value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })} {...props} />
    </div>
  );

  return (
    <div className="relative min-h-dvh">
      <div className="absolute inset-x-0 top-0 h-40 overflow-hidden bg-panel tile-on-panel" aria-hidden="true">
        <TilePattern className="absolute inset-0" id="setup-tile" />
      </div>
      <main className="relative mx-auto max-w-lg px-4 pt-24 pb-10">
        <div className="rounded-xl border bg-card p-6 shadow-sm sm:p-8">
          <BrandMark className="text-lg" />
          <h1 className="mt-6 text-2xl font-extrabold">{t("setup.title")}</h1>

          {status.isLoading && <p className="mt-4 text-muted-foreground">{t("common.loading")}</p>}

          {(done || status.data?.needsSetup === false) && (
            <div className="mt-6 space-y-4">
              <p className="flex items-start gap-2">
                <CheckCircle2 className="mt-1 size-5 shrink-0 text-success" aria-hidden="true" />
                {done ? t("setup.done") : t("setup.alreadyDone")}
              </p>
              <Button asChild className="h-12 w-full">
                <Link href="/login">{t("login.title")}</Link>
              </Button>
            </div>
          )}

          {status.data?.needsSetup && !done && (
            <form onSubmit={submit} className="mt-2 space-y-5">
              <p className="text-muted-foreground">{t("setup.subtitle")}</p>
              {field("token", t("setup.token"), { autoComplete: "off", dir: "ltr", required: true, "data-testid": "setup-token" } as React.ComponentProps<"input">)}
              {field("displayName", t("setup.name"), { required: true, autoComplete: "name" })}
              {field("phone", t("setup.phone"), { type: "tel", inputMode: "tel", dir: "ltr", className: "h-12 text-end", required: true })}
              {field("brandName", t("setup.brand"))}
              <ErrorText error={error} />
              <Button type="submit" className="h-12 w-full text-base" disabled={busy}>
                {busy && <Loader2 className="animate-spin" aria-hidden="true" />}
                {t("setup.submit")}
              </Button>
            </form>
          )}
          <ErrorText error={status.error} />
        </div>
      </main>
    </div>
  );
}
