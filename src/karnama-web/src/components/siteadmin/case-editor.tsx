"use client";

import { ExternalLink, ImagePlus, Loader2, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { useErrorMessage } from "@/components/app/error-text";
import { JalaliDateField, NumberField, TextArea, TextField, Toggle } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { api, uploadForm } from "@/lib/api";
import { API_BASE } from "@/lib/config";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { prepareImage } from "@/components/blog/image-prep";
import { BlurTool } from "./blur-tool";
import type { CaseItem, CaseMetric, CaseShot } from "./types";

type Form = Omit<CaseItem, "id" | "slug" | "isSample" | "published" | "version"> & { slug: string };
const empty: Form = {
  businessName: "", slug: "", industry: null, city: "شیراز", logo: null, logoUrl: null, handle: null, from: null, to: null, services: [], summary: null,
  followersBefore: null, followersAfter: null, revenueBefore: null, revenueAfter: null, revenueAsPercent: true, metrics: [], shots: [], quote: null, quoteBy: null,
  videoUrl: null, source: null, consentObtained: false, consentFile: null, featured: false, sortOrder: 0,
};
const day = (iso: string | null) => (iso ? `${iso.slice(0, 10)}T00:00:00+03:30` : null);
const toDate = (v: string | null) => (v ? v.slice(0, 10) : null);

async function upload(blob: Blob, consent: boolean) {
  const form = new FormData();
  form.append("file", blob, "image.webp");
  return uploadForm<{ id: string; url: string }>(`/api/v1/cases/media${consent ? "?consent=true" : ""}`, form);
}

export function CaseEditor({ item, onClose, onSaved }: { item: CaseItem | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [f, setF] = useState<Form>(item ? { ...item } : empty);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [blurFile, setBlurFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState<string | null>(null);
  const L = (k: TextKey) => t(k);
  const set = (p: Partial<Form>) => setF({ ...f, ...p });
  const nz = (s: string) => (s.trim() === "" ? null : s);
  // Revenue is entered in tomans and stored in rials (like every price in the app).
  const rial = (v: number | null) => (v === null ? null : v * 10);
  const tomanOf = (v: number | null) => (v === null ? null : Math.round(v / 10));

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const body = { ...f, slug: f.slug || null, services: f.services, version: item?.version };
      await api(item ? `/api/v1/cases/${item.id}` : "/api/v1/cases", { method: item ? "PUT" : "POST", body });
      notify.success(t("cases.saved"));
      onSaved();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const pick = async (file: File | undefined, kind: "logo" | "consent") => {
    if (!file) return;
    setUploading(kind);
    try {
      const { blob } = await prepareImage(file, kind === "logo" ? 400 : 1600);
      const r = await upload(blob, kind === "consent");
      if (kind === "logo") set({ logo: r.id, logoUrl: r.url });
      else set({ consentFile: r.id, consentObtained: true });
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setUploading(null);
    }
  };

  const setShot = (i: number, p: Partial<CaseShot>) => set({ shots: f.shots.map((s, j) => (j === i ? { ...s, ...p } : s)) });
  const setMetric = (i: number, p: Partial<CaseMetric>) => set({ metrics: f.metrics.map((m, j) => (j === i ? { ...m, ...p } : m)) });

  return (
    <EditorSheet title={item ? t("cases.edit") : t("cases.new")} onClose={onClose} onSubmit={save} busy={busy} error={error} wide testId="case-editor">
      <div className="grid gap-5 sm:grid-cols-2">
        <TextField label={L("cases.business")} value={f.businessName} onChange={(v) => set({ businessName: v })} testId="case-name" />
        <TextField label={L("cases.industry")} value={f.industry ?? ""} onChange={(v) => set({ industry: nz(v) })} optional />
        <TextField label={L("cases.city")} value={f.city ?? ""} onChange={(v) => set({ city: nz(v) })} optional />
        <TextField label={L("cases.handle")} dir="ltr" value={f.handle ?? ""} onChange={(v) => set({ handle: nz(v) })} optional />
        <JalaliDateField label={L("cases.from")} value={day(f.from)} onChange={(v) => set({ from: toDate(v) })} optional />
        <JalaliDateField label={L("cases.to")} value={day(f.to)} onChange={(v) => set({ to: toDate(v) })} optional />
      </div>
      <TextField label={L("cases.services")} help={L("cases.servicesHelp")} value={f.services.join("، ")} onChange={(v) => set({ services: v.split(/[,،]/).map((x) => x.trim()).filter(Boolean) })} optional />
      <TextArea label={L("cases.summary")} value={f.summary ?? ""} onChange={(v) => set({ summary: nz(v) })} rows={3} optional />

      <fieldset className="space-y-4 rounded-lg border p-4">
        <legend className="px-1 font-bold">{t("cases.numbers")}</legend>
        <div className="grid gap-4 sm:grid-cols-2">
          <NumberField label={L("cases.followersBefore")} value={f.followersBefore} onChange={(v) => set({ followersBefore: v })} optional min={0} testId="case-fb" />
          <NumberField label={L("cases.followersAfter")} value={f.followersAfter} onChange={(v) => set({ followersAfter: v })} optional min={0} testId="case-fa" />
          <NumberField label={L("cases.revenueBefore")} value={tomanOf(f.revenueBefore)} onChange={(v) => set({ revenueBefore: rial(v) })} optional min={0} />
          <NumberField label={L("cases.revenueAfter")} value={tomanOf(f.revenueAfter)} onChange={(v) => set({ revenueAfter: rial(v) })} optional min={0} />
        </div>
        <Toggle label={L("cases.revenueAsPercent")} checked={f.revenueAsPercent} onChange={(v) => set({ revenueAsPercent: v })} />
        <TextField label={L("cases.source")} help={L("cases.sourceHelp")} value={f.source ?? ""} onChange={(v) => set({ source: nz(v) })} optional />
        <div className="space-y-2">
          <p className="text-sm font-medium">{t("cases.metrics")}</p>
          {f.metrics.map((m, i) => (
            <div key={i} className="grid grid-cols-[1fr_6rem_6rem_auto] items-end gap-2">
              <input aria-label={t("cases.metricLabel")} placeholder={t("cases.metricLabel")} value={m.label} onChange={(e) => setMetric(i, { label: e.target.value })} className="h-11 rounded-md border bg-transparent px-2" />
              <input aria-label={t("cases.metricBefore")} placeholder={t("cases.metricBefore")} inputMode="numeric" dir="ltr" value={m.before ?? ""} onChange={(e) => setMetric(i, { before: e.target.value ? Number(e.target.value) : null })} className="h-11 rounded-md border bg-transparent px-2" />
              <input aria-label={t("cases.metricAfter")} placeholder={t("cases.metricAfter")} inputMode="numeric" dir="ltr" value={m.after ?? ""} onChange={(e) => setMetric(i, { after: e.target.value ? Number(e.target.value) : null })} className="h-11 rounded-md border bg-transparent px-2" />
              <Button type="button" variant="ghost" size="icon" className="size-11" onClick={() => set({ metrics: f.metrics.filter((_, j) => j !== i) })} aria-label={t("home.remove")}>
                <Trash2 className="size-4" aria-hidden="true" />
              </Button>
            </div>
          ))}
          {f.metrics.length < 8 && (
            <Button type="button" variant="outline" size="sm" onClick={() => set({ metrics: [...f.metrics, { label: "", before: null, after: null, unit: null }] })}>
              <Plus aria-hidden="true" />
              {t("cases.addMetric")}
            </Button>
          )}
        </div>
      </fieldset>

      <fieldset className="space-y-3 rounded-lg border p-4">
        <legend className="px-1 font-bold">{t("cases.shots")}</legend>
        <p className="text-sm text-muted-foreground">{t("cases.shotsHelp")}</p>
        <ul className="grid gap-3 sm:grid-cols-2">
          {f.shots.map((s, i) => (
            <li key={s.image} className="space-y-2 rounded-lg border p-2">
              {/* eslint-disable-next-line @next/next/no-img-element -- uploaded screenshot */}
              <img src={s.url} alt="" className="max-h-40 w-full rounded object-contain" />
              <input value={s.caption ?? ""} onChange={(e) => setShot(i, { caption: nz(e.target.value) })} placeholder={t("cases.shotCaption")} aria-label={t("cases.shotCaption")} className="h-10 w-full rounded-md border bg-transparent px-2 text-sm" />
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" checked={s.isAfter} onChange={(e) => setShot(i, { isAfter: e.target.checked })} className="size-4" />
                {t("cases.shotAfter")}
              </label>
              <Button type="button" variant="ghost" size="sm" className="text-destructive" onClick={() => set({ shots: f.shots.filter((_, j) => j !== i) })}>
                {t("home.remove")}
              </Button>
            </li>
          ))}
        </ul>
        {blurFile ? (
          <BlurTool
            file={blurFile}
            onCancel={() => setBlurFile(null)}
            onDone={async (blob) => {
              try {
                const r = await upload(blob, false);
                set({ shots: [...f.shots, { image: r.id, url: r.url, caption: null, takenOn: null, isAfter: true }] });
                setBlurFile(null);
              } catch (e) {
                notify.error(errorMessage(e));
              }
            }}
          />
        ) : (
          f.shots.length < 12 && (
            <label className="inline-flex h-11 cursor-pointer items-center gap-2 rounded-md border px-3 text-sm hover:bg-muted">
              <ImagePlus className="size-4" aria-hidden="true" />
              {t("cases.addShot")}
              <input type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={(e) => setBlurFile(e.target.files?.[0] ?? null)} data-testid="case-shot-input" />
            </label>
          )
        )}
      </fieldset>

      <div className="grid gap-5 sm:grid-cols-2">
        <TextArea label={L("cases.quote")} value={f.quote ?? ""} onChange={(v) => set({ quote: nz(v) })} rows={3} optional />
        <div className="space-y-5">
          <TextField label={L("cases.quoteBy")} value={f.quoteBy ?? ""} onChange={(v) => set({ quoteBy: nz(v) })} optional />
          <TextField label={L("cases.video")} dir="ltr" value={f.videoUrl ?? ""} onChange={(v) => set({ videoUrl: nz(v) })} optional placeholder="https://" />
        </div>
      </div>
      <div className="space-y-2">
        <p className="text-sm font-medium">{t("cases.logo")}</p>
        <div className="flex items-center gap-3">
          {/* eslint-disable-next-line @next/next/no-img-element -- uploaded logo */}
          {f.logoUrl && <img src={f.logoUrl} alt="" className="size-14 rounded-md border object-contain" />}
          <label className="inline-flex h-11 cursor-pointer items-center gap-2 rounded-md border px-3 text-sm hover:bg-muted">
            {uploading === "logo" ? <Loader2 className="size-4 animate-spin" aria-hidden="true" /> : <ImagePlus className="size-4" aria-hidden="true" />}
            {t("editor.upload" as TextKey)}
            <input type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={(e) => void pick(e.target.files?.[0], "logo")} />
          </label>
        </div>
      </div>

      <fieldset className="space-y-3 rounded-lg border-2 border-foreground p-4" data-testid="case-consent">
        <legend className="px-1 font-bold">{t("cases.consent")}</legend>
        <p className="text-sm text-muted-foreground">{t("cases.consentHelp")}</p>
        <div className="flex flex-wrap items-center gap-3">
          <label className="inline-flex h-11 cursor-pointer items-center gap-2 rounded-md border px-3 text-sm hover:bg-muted">
            {uploading === "consent" ? <Loader2 className="size-4 animate-spin" aria-hidden="true" /> : <ImagePlus className="size-4" aria-hidden="true" />}
            {t("cases.consentFile")}
            <input type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={(e) => void pick(e.target.files?.[0], "consent")} data-testid="case-consent-input" />
          </label>
          {f.consentFile && (
            <a className="inline-flex items-center gap-1 text-sm font-semibold underline" href={`${API_BASE}/api/v1/cases/consent/${f.consentFile.split("/")[1]}`} target="_blank" rel="noopener noreferrer">
              <ExternalLink className="size-4" aria-hidden="true" />
              {t("cases.consentView")}
            </a>
          )}
        </div>
        <Toggle label={L("cases.consentObtained")} checked={f.consentObtained} onChange={(v) => set({ consentObtained: v })} />
      </fieldset>
      <div className="grid gap-5 sm:grid-cols-2">
        <Toggle label={L("cases.featured")} checked={f.featured} onChange={(v) => set({ featured: v })} />
        <NumberField label={L("cases.sort")} value={f.sortOrder} onChange={(v) => set({ sortOrder: v ?? 0 })} min={0} />
      </div>
    </EditorSheet>
  );
}
