"use client";

import { useQueryClient } from "@/lib/query";
import { ImageUp, Loader2, RotateCcw, Trash2 } from "lucide-react";
import { useRef, useState } from "react";
import { BrandMark } from "@/components/app/brand-mark";
import { ErrorText, useErrorMessage } from "@/components/app/error-text";
import { FieldHelp } from "@/components/app/field-help";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { api, uploadForm } from "@/lib/api";
import { brandVars, contrast, DEFAULT_COLORS, isHex, PRESETS, type Brand, type BrandColors } from "@/lib/brand";
import { faSettings } from "@/lib/i18n/fa-settings";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { BRAND_QUERY, useBrand } from "@/lib/use-brand";

registerTexts(faSettings);

export default function BrandSettingsPage() {
  const allowed = useRequire([{ code: "settings.manage", scope: "all" }]);
  const brand = useBrand();
  if (!allowed || !brand.loaded) return null;
  return <BrandForm key={brand.version} initial={brand} />;
}

function BrandForm({ initial }: { initial: ReturnType<typeof useBrand> }) {
  const { t } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const [name, setName] = useState(initial.name ?? "");
  const [tagline, setTagline] = useState(initial.tagline ?? "");
  const [colors, setColors] = useState<BrandColors>(initial.colors);
  const [busy, setBusy] = useState(false);
  const [logoBusy, setLogoBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const file = useRef<HTMLInputElement>(null);

  const valid = isHex(colors.primary) && isHex(colors.dark) && (!colors.emphasis || isHex(colors.emphasis));
  const preview = valid ? brandVars(colors).light : null;

  const save = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const saved = await api<Brand>("/api/v1/settings/brand", {
        method: "PUT",
        body: { name: name || null, tagline: tagline || null, colors: { ...colors, emphasis: colors.emphasis || null }, version: initial.version },
      });
      qc.setQueryData(BRAND_QUERY, saved);
      notify.success(t("brandSettings.saved"));
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };

  const uploadLogo = async (f: File) => {
    if (f.size > 2 * 1024 * 1024 || !/^image\/(png|jpeg|webp)$/.test(f.type)) {
      notify.error(t("brandSettings.logoHelp"));
      return;
    }
    setLogoBusy(true);
    try {
      const form = new FormData();
      form.append("file", f);
      const saved = await uploadForm<Brand>("/api/v1/settings/brand/logo", form);
      qc.setQueryData(BRAND_QUERY, saved);
      notify.success(t("brandSettings.logoSaved"));
    } catch (err) {
      notify.error(errorMessage(err));
    } finally {
      setLogoBusy(false);
      if (file.current) file.current.value = "";
    }
  };

  const removeLogo = async () => {
    setLogoBusy(true);
    try {
      qc.setQueryData(BRAND_QUERY, await api<Brand>("/api/v1/settings/brand/logo", { method: "DELETE" }));
    } catch (err) {
      notify.error(errorMessage(err));
    } finally {
      setLogoBusy(false);
    }
  };

  return (
    <form onSubmit={save} className="max-w-3xl space-y-8 pb-24 md:pb-0" noValidate>
      <header>
        <h1 className="text-2xl font-extrabold">{t("brandSettings.title")}</h1>
        <p className="mt-1 text-muted-foreground">{t("brandSettings.subtitle")}</p>
      </header>

      <section className="space-y-5 rounded-xl border bg-card p-5">
        <div className="space-y-2">
          <Label htmlFor="brand-name">{t("brandSettings.name")}</Label>
          <Input id="brand-name" className="h-12" value={name} maxLength={80} onChange={(e) => setName(e.target.value)} aria-describedby="brand-name-help" data-testid="brand-name" />
          <FieldHelp id="brand-name-help">{t("brandSettings.nameHelp")}</FieldHelp>
        </div>
        <div className="space-y-2">
          <Label htmlFor="brand-tagline">{t("brandSettings.tagline")}</Label>
          <Input id="brand-tagline" className="h-12" value={tagline} maxLength={160} onChange={(e) => setTagline(e.target.value)} aria-describedby="brand-tagline-help" />
          <FieldHelp id="brand-tagline-help">{t("brandSettings.taglineHelp")}</FieldHelp>
        </div>
      </section>

      <section className="space-y-3 rounded-xl border bg-card p-5" aria-labelledby="logo-title">
        <h2 id="logo-title" className="font-bold">
          {t("brandSettings.logo")}
        </h2>
        <div className="flex flex-wrap items-center gap-4">
          {initial.logo ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={initial.logo} alt={t("brandSettings.logo")} className="size-20 rounded-lg border bg-muted object-contain p-1" />
          ) : (
            <p className="max-w-xs text-sm text-muted-foreground">{t("brandSettings.logoNone")}</p>
          )}
          <input ref={file} type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" id="logo-file" onChange={(e) => e.target.files?.[0] && uploadLogo(e.target.files[0])} data-testid="brand-logo-input" />
          <Button type="button" variant="outline" className="h-12" disabled={logoBusy} onClick={() => file.current?.click()}>
            {logoBusy ? <Loader2 className="animate-spin" aria-hidden="true" /> : <ImageUp aria-hidden="true" />}
            {t("brandSettings.logoUpload")}
          </Button>
          {initial.logo && (
            <Button type="button" variant="ghost" className="h-12 text-destructive" disabled={logoBusy} onClick={removeLogo}>
              <Trash2 aria-hidden="true" />
              {t("brandSettings.logoRemove")}
            </Button>
          )}
        </div>
        <FieldHelp id="logo-help">{t("brandSettings.logoHelp")}</FieldHelp>
      </section>

      <section className="space-y-5 rounded-xl border bg-card p-5" aria-labelledby="colors-title">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="colors-title" className="font-bold">
            {t("brandSettings.colors")}
          </h2>
          <Button type="button" variant="ghost" className="h-11" onClick={() => setColors(DEFAULT_COLORS)}>
            <RotateCcw aria-hidden="true" />
            {t("brandSettings.reset")}
          </Button>
        </div>
        {/* Ready-made palettes from 21st.dev themes: one click fills both colours (still editable below). */}
        <div className="space-y-2">
          <p className="text-sm font-bold">{t("brandSettings.presets")}</p>
          <p className="text-xs leading-6 text-muted-foreground">{t("brandSettings.presetsHelp")}</p>
          <ul className="grid grid-cols-2 gap-2 sm:grid-cols-4" data-testid="brand-presets">
            {PRESETS.map((p) => {
              const on = colors.primary.toLowerCase() === p.primary && colors.dark.toLowerCase() === p.dark;
              return (
                <li key={p.id}>
                  <button
                    type="button"
                    aria-pressed={on}
                    onClick={() => setColors({ primary: p.primary, dark: p.dark, emphasis: null })}
                    className="flex w-full flex-col gap-2 rounded-xl border p-2 text-start transition-shadow hover:shadow-md aria-pressed:ring-2 aria-pressed:ring-ring"
                  >
                    <span className="flex h-10 overflow-hidden rounded-lg" aria-hidden="true">
                      <span className="flex-[2]" style={{ background: p.dark }} />
                      <span className="flex-1" style={{ background: p.primary }} />
                    </span>
                    <span className="truncate text-xs font-bold" dir="ltr">{p.theme}</span>
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
        <ColorField id="primary" label={t("brandSettings.primary")} help={t("brandSettings.primaryHelp")} value={colors.primary} onChange={(v) => setColors({ ...colors, primary: v })} />
        {isHex(colors.primary) && contrast(colors.primary, "#ffffff") < 3 && <p className="text-sm text-muted-foreground">{t("brandSettings.contrastWarn")}</p>}
        <ColorField id="dark" label={t("brandSettings.dark")} help={t("brandSettings.darkHelp")} value={colors.dark} onChange={(v) => setColors({ ...colors, dark: v })} />
        <ColorField id="emphasis" label={t("brandSettings.emphasis")} help={t("brandSettings.emphasisHelp")} value={colors.emphasis ?? ""} optional onChange={(v) => setColors({ ...colors, emphasis: v || null })} />
        <p className="text-sm text-muted-foreground">{t("brandSettings.autoContrast")}</p>

        {preview && (
          <div className="rounded-lg border p-4" style={preview as React.CSSProperties} aria-label={t("brandSettings.preview")}>
            <p className="mb-3 text-sm font-bold">{t("brandSettings.preview")}</p>
            <div className="flex flex-wrap items-center gap-4">
              <span className="inline-flex items-center gap-3 rounded-lg bg-[var(--panel)] px-4 py-3 text-[var(--panel-foreground)]">
                <BrandMark />
              </span>
              <span className="inline-flex h-11 items-center rounded-md border border-[var(--primary-border)] bg-[var(--primary)] px-4 font-medium text-[var(--primary-foreground)]">
                {t("brandSettings.previewButton")}
              </span>
              <span className="font-bold text-[var(--emphasis)]">{t("brandSettings.previewLink")}</span>
            </div>
          </div>
        )}
      </section>

      <div className="safe-bottom fixed inset-x-0 bottom-16 z-10 border-t bg-background/95 p-4 backdrop-blur md:static md:border-0 md:bg-transparent md:p-0">
        <ErrorText error={error} />
        <Button type="submit" className="h-12 w-full md:w-auto md:min-w-48" disabled={busy || !valid} data-testid="brand-save">
          {busy && <Loader2 className="animate-spin" aria-hidden="true" />}
          {busy ? t("common.saving") : t("common.save")}
        </Button>
      </div>
    </form>
  );
}

function ColorField({ id, label, help, value, optional, onChange }: { id: string; label: string; help: string; value: string; optional?: boolean; onChange: (v: string) => void }) {
  const ok = (optional && !value) || isHex(value);
  return (
    <div className="space-y-2">
      <Label htmlFor={`color-${id}`}>{label}</Label>
      <div className="flex items-center gap-3">
        <input
          type="color"
          aria-label={label}
          value={isHex(value) ? value : "#000000"}
          onChange={(e) => onChange(e.target.value.toUpperCase())}
          className="h-12 w-14 cursor-pointer rounded-md border bg-card p-1"
        />
        <Input
          id={`color-${id}`}
          dir="ltr"
          className="h-12 w-36 font-mono uppercase"
          value={value}
          placeholder={optional ? "#______" : "#RRGGBB"}
          maxLength={7}
          aria-invalid={!ok}
          aria-describedby={`color-${id}-help`}
          data-testid={`brand-color-${id}`}
          onChange={(e) => onChange(e.target.value.trim())}
        />
      </div>
      <FieldHelp id={`color-${id}-help`}>{help}</FieldHelp>
    </div>
  );
}
