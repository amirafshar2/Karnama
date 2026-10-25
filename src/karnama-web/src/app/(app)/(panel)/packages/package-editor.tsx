"use client";

import { useQuery } from "@/lib/query";
import { ImageUp, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { JalaliDateField, NumberField, SelectField, TextArea, TextField, Toggle, toRial, toToman } from "@/components/app/form";
import { useErrorMessage } from "@/components/app/error-text";
import { Button } from "@/components/ui/button";
import { api, uploadForm } from "@/lib/api";
import { API_BASE } from "@/lib/config";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { faM2Forms } from "@/lib/i18n/fa-m2-forms";
import { registerTexts as registerFormTexts } from "@/lib/i18n/registry";

registerFormTexts(faM2Forms);

type L = Record<string, string>;
type Quota = { serviceTypeId: string; count: number };
type Pkg = {
  id: string;
  nameValues: L;
  slug: string;
  sku: string;
  category: string | null;
  tags: string[];
  shortDescription: L;
  descriptionMarkdown: string | null;
  price: number;
  compareAtPrice: number | null;
  specialPrice: number | null;
  specialFrom: string | null;
  specialTo: string | null;
  period: string;
  validityDays: number;
  saleFrom: string | null;
  saleTo: string | null;
  capacity: number | null;
  platformIds: string[];
  defaultManagement: string;
  access: string;
  consultSessions: number;
  revisionsPerItem: number;
  deliveryHours: number;
  features: L[];
  faq: { question: string; answer: string }[];
  status: string;
  showOnSite: boolean;
  sortOrder: number;
  badge: string;
  seoTitle: string | null;
  seoDescription: string | null;
  quotas: Quota[];
  coverUrl: string | null;
  version: number;
};

const EMPTY: Pkg = {
  id: "", nameValues: {}, slug: "", sku: "", category: null, tags: [], shortDescription: {}, descriptionMarkdown: null,
  price: 0, compareAtPrice: null, specialPrice: null, specialFrom: null, specialTo: null, period: "monthly", validityDays: 30,
  saleFrom: null, saleTo: null, capacity: null, platformIds: [], defaultManagement: "agencyManaged", access: "fullAccess",
  consultSessions: 0, revisionsPerItem: 2, deliveryHours: 48, features: [], faq: [], status: "draft", showOnSite: true,
  sortOrder: 100, badge: "none", seoTitle: null, seoDescription: null, quotas: [], coverUrl: null, version: 0,
};

const opts = <T extends string>(t: (k: TextKey) => string, prefix: string, values: T[]) => values.map((v) => ({ value: v, label: t(`${prefix}.${v}` as TextKey) }));

type Props = { id: string; onClose: () => void; onSaved: (created: boolean) => void };

export function PackageEditor(props: Props) {
  const { t } = useI18n();
  const isNew = props.id === "new";
  const existing = useQuery({ queryKey: ["package", props.id], enabled: !isNew, queryFn: () => api<Pkg>(`/api/v1/packages/${props.id}`), gcTime: 0 });
  if (!isNew && !existing.data) return <EditorSheet title={t("packages.formEdit")} onClose={props.onClose} onSubmit={() => {}} busy={false} error={existing.error} loading={!existing.error} wide>{null}</EditorSheet>;
  return <PackageForm {...props} initial={existing.data ?? EMPTY} />;
}

function PackageForm({ id, onClose, onSaved, initial }: Props & { initial: Pkg }) {
  const { t, lang } = useI18n();
  const errorMessage = useErrorMessage();
  const isNew = id === "new";
  const services = useQuery({ queryKey: ["services", "options"], queryFn: () => api<{ id: string; name: string }[]>("/api/v1/services/options") });
  const platforms = useQuery({ queryKey: ["platforms"], queryFn: () => api<{ id: string; name: string; isActive: boolean }[]>("/api/v1/platforms") });
  const [p, setP] = useState<Pkg | null>(initial);
  const [features, setFeatures] = useState(() => initial.features.map((f) => f[lang] ?? Object.values(f)[0] ?? "").join("\n"));
  const [faq, setFaq] = useState(() => initial.faq.map((f) => `${f.question}\n${f.answer}`).join("\n"));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const set = (patch: Partial<Pkg>) => setP((x) => (x ? { ...x, ...patch } : x));

  const submit = async () => {
    if (!p) return;
    setBusy(true);
    setError(null);
    const lines = faq.split("\n").map((l) => l.trim()).filter(Boolean);
    const faqList = [];
    for (let i = 0; i + 1 < lines.length; i += 2) faqList.push({ question: lines[i], answer: lines[i + 1] });
    const body = {
      name: p.nameValues, slug: p.slug, sku: p.sku, category: p.category, tags: p.tags, shortDescription: p.shortDescription,
      descriptionMarkdown: p.descriptionMarkdown, price: p.price, compareAtPrice: p.compareAtPrice, specialPrice: p.specialPrice,
      specialFrom: p.specialFrom, specialTo: p.specialTo, period: p.period, validityDays: p.validityDays, saleFrom: p.saleFrom,
      saleTo: p.saleTo, capacity: p.capacity, platformIds: p.platformIds, defaultManagement: p.defaultManagement, access: p.access,
      consultSessions: p.consultSessions, revisionsPerItem: p.revisionsPerItem, deliveryHours: p.deliveryHours,
      features: features.split("\n").map((l) => l.trim()).filter(Boolean).map((l) => ({ [lang]: l })),
      faq: faqList, status: p.status, showOnSite: p.showOnSite, sortOrder: p.sortOrder, badge: p.badge,
      seoTitle: p.seoTitle || null, seoDescription: p.seoDescription || null,
      quotas: p.quotas.filter((q) => q.serviceTypeId && q.count > 0), version: isNew ? null : p.version,
    };
    try {
      await api(isNew ? "/api/v1/packages" : `/api/v1/packages/${id}`, { method: isNew ? "POST" : "PUT", body });
      onSaved(isNew);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const uploadCover = async (file: File) => {
    const form = new FormData();
    form.append("file", file);
    try {
      const saved = await uploadForm<Pkg>(`/api/v1/packages/${id}/cover`, form);
      set({ coverUrl: saved.coverUrl, version: saved.version });
      notify.success(t("crud.saved"));
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };

  const serviceOptions = (services.data ?? []).map((s) => ({ value: s.id, label: s.name }));

  return (
    <EditorSheet title={isNew ? t("packages.formCreate") : t("packages.formEdit")} onClose={onClose} onSubmit={submit} busy={busy} error={error} loading={!p} wide testId="package-editor">
      {p && (
        <>
          <Section title={t("packages.section.basic")}>
            <TextField label={t("packages.name")} help={t("packages.nameHelp")} value={p.nameValues[lang] ?? ""} onChange={(v) => set({ nameValues: { ...p.nameValues, [lang]: v } })} testId="package-name" />
            <TextField label={t("packages.sku")} help={t("packages.skuHelp")} value={p.sku} onChange={(v) => set({ sku: v })} dir="ltr" testId="package-sku" />
            <TextField label={t("packages.category")} help={t("packages.categoryHelp")} value={p.category ?? ""} onChange={(v) => set({ category: v || null })} optional />
            <TextField label={t("packages.short")} help={t("packages.shortHelp")} value={p.shortDescription[lang] ?? ""} onChange={(v) => set({ shortDescription: { ...p.shortDescription, [lang]: v } })} optional />
            <SelectField label={t("packages.status")} help={t("packages.statusHelp")} value={p.status} onChange={(v) => set({ status: v })} options={opts(t, "packages.status", ["draft", "active", "archived"])} testId="package-status" />
          </Section>

          <Section title={t("packages.section.price")}>
            <NumberField label={t("packages.price")} help={t("packages.priceHelp")} value={toToman(p.price)} onChange={(v) => set({ price: toRial(v) ?? 0 })} min={0} testId="package-price" />
            <NumberField label={t("packages.special")} help={t("packages.specialHelp")} value={toToman(p.specialPrice)} onChange={(v) => set({ specialPrice: toRial(v) })} optional min={0} />
            {p.specialPrice !== null && (
              <>
                <JalaliDateField label={t("packages.specialFrom")} help={t("packages.specialFromHelp")} value={p.specialFrom} onChange={(v) => set({ specialFrom: v })} optional />
                <JalaliDateField label={t("packages.specialTo")} help={t("packages.specialToHelp")} value={p.specialTo} onChange={(v) => set({ specialTo: v })} optional />
              </>
            )}
            <SelectField label={t("packages.period")} help={t("packages.periodHelp")} value={p.period} onChange={(v) => set({ period: v })} options={opts(t, "packages.period", ["monthly", "quarterly", "oneTime"])} />
            <NumberField label={t("packages.validity")} help={t("packages.validityHelp")} value={p.validityDays} onChange={(v) => set({ validityDays: v ?? 30 })} min={1} max={3650} testId="package-validity" />
            <JalaliDateField label={t("packages.saleFrom")} help={t("packages.saleHelp")} value={p.saleFrom} onChange={(v) => set({ saleFrom: v })} optional />
            <JalaliDateField label={t("packages.saleTo")} help={t("packages.saleToHelp")} value={p.saleTo} onChange={(v) => set({ saleTo: v })} optional />
            <NumberField label={t("packages.capacity")} help={t("packages.capacityHelp")} value={p.capacity} onChange={(v) => set({ capacity: v })} optional min={1} />
          </Section>

          <Section title={t("packages.section.content")} full>
            <div className="space-y-2">
              <p className="text-sm font-medium">{t("packages.quotas")}</p>
              <p className="text-sm text-muted-foreground">{t("packages.quotasHelp")}</p>
              {p.quotas.map((q, i) => (
                <div key={i} className="flex items-end gap-2" data-testid={`quota-${i}`}>
                  <div className="flex-1">
                    <SelectField label={t("services.title")} help={t("packages.quotaServiceHelp")} value={q.serviceTypeId} onChange={(v) => set({ quotas: p.quotas.map((x, j) => (j === i ? { ...x, serviceTypeId: v } : x)) })} options={serviceOptions} optional />
                  </div>
                  <div className="w-28">
                    <NumberField label="#" help={t("packages.quotaCountHelp")} value={q.count} onChange={(v) => set({ quotas: p.quotas.map((x, j) => (j === i ? { ...x, count: v ?? 0 } : x)) })} min={0} max={1000} />
                  </div>
                  <Button type="button" variant="ghost" size="icon" className="mb-0.5 size-12 text-destructive" aria-label={t("crud.remove")} onClick={() => set({ quotas: p.quotas.filter((_, j) => j !== i) })}>
                    <Trash2 aria-hidden="true" />
                  </Button>
                </div>
              ))}
              <Button type="button" variant="outline" className="h-12 md:h-11" onClick={() => set({ quotas: [...p.quotas, { serviceTypeId: "", count: 1 }] })} data-testid="quota-add">
                <Plus aria-hidden="true" />
                {t("packages.addQuota")}
              </Button>
            </div>
            <fieldset className="space-y-2">
              <legend className="text-sm font-medium">{t("packages.platforms")}</legend>
              <div className="flex flex-wrap gap-2">
                {(platforms.data ?? []).filter((x) => x.isActive).map((pl) => (
                  <label key={pl.id} className="flex min-h-11 cursor-pointer items-center gap-2 rounded-md border px-3 text-sm">
                    <input type="checkbox" className="size-5" checked={p.platformIds.includes(pl.id)} onChange={(e) => set({ platformIds: e.target.checked ? [...p.platformIds, pl.id] : p.platformIds.filter((x) => x !== pl.id) })} />
                    {pl.name}
                  </label>
                ))}
              </div>
            </fieldset>
            <div className="grid gap-5 sm:grid-cols-2">
              <SelectField label={t("packages.access")} help={t("packages.accessHelp")} value={p.access} onChange={(v) => set({ access: v })} options={opts(t, "packages.access", ["fullAccess", "usernameOnly"])} />
              <SelectField label={t("packages.management")} help={t("packages.managementHelp")} value={p.defaultManagement} onChange={(v) => set({ defaultManagement: v })} options={opts(t, "clients.management", ["agencyManaged", "clientManaged"])} />
              <NumberField label={t("packages.consult")} help={t("packages.consultHelp")} value={p.consultSessions} onChange={(v) => set({ consultSessions: v ?? 0 })} min={0} />
              <NumberField label={t("packages.revisions")} help={t("packages.revisionsHelp")} value={p.revisionsPerItem} onChange={(v) => set({ revisionsPerItem: v ?? 0 })} min={0} />
              <NumberField label={t("packages.delivery")} help={t("packages.deliveryHelp")} value={p.deliveryHours} onChange={(v) => set({ deliveryHours: v ?? 0 })} min={0} />
            </div>
          </Section>

          <Section title={t("packages.section.description")} full>
            <TextArea label={t("packages.description")} help={t("packages.descriptionHelp")} value={p.descriptionMarkdown ?? ""} onChange={(v) => set({ descriptionMarkdown: v || null })} rows={6} optional />
            <TextArea label={t("packages.features")} help={t("packages.featuresHelp")} value={features} onChange={setFeatures} rows={4} optional />
            <TextArea label={t("packages.faq")} help={t("packages.faqHelp")} value={faq} onChange={setFaq} rows={4} optional />
          </Section>

          <Section title={t("packages.section.site")}>
            <Toggle label={t("packages.showOnSite")} help={t("packages.showOnSiteHelp")} checked={p.showOnSite} onChange={(v) => set({ showOnSite: v })} />
            <SelectField label={t("packages.badge")} help={t("packages.badgeHelp")} value={p.badge} onChange={(v) => set({ badge: v })} options={opts(t, "packages.badge", ["none", "bestSeller", "new"])} />
            <TextField label={t("packages.slug")} help={t("packages.slugHelp")} value={p.slug} onChange={(v) => set({ slug: v })} optional />
            <NumberField label={t("crud.sortOrder")} help={t("crud.sortOrderHelp")} value={p.sortOrder} onChange={(v) => set({ sortOrder: v ?? 0 })} />
            <TextField label={t("packages.seoTitle")} help={t("packages.seoTitleHelp")} value={p.seoTitle ?? ""} onChange={(v) => set({ seoTitle: v })} maxLength={70} optional />
            <TextField label={t("packages.seoDescription")} help={t("packages.seoDescriptionHelp")} value={p.seoDescription ?? ""} onChange={(v) => set({ seoDescription: v })} maxLength={170} optional />
            {!isNew && (
              <div className="space-y-2 sm:col-span-2">
                <p className="text-sm font-medium">{t("packages.cover")}</p>
                <div className="flex items-center gap-4">
                  {/* eslint-disable-next-line @next/next/no-img-element -- static export; media is served by the API */}
                  {p.coverUrl && <img src={API_BASE + p.coverUrl} alt="" className="h-20 w-32 rounded-md border object-cover" />}
                  <label className="inline-flex h-12 cursor-pointer items-center gap-2 rounded-md border px-4 text-sm font-medium hover:bg-accent">
                    <ImageUp className="size-4" aria-hidden="true" />
                    {t("packages.cover")}
                    <input type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={(e) => e.target.files?.[0] && uploadCover(e.target.files[0])} />
                  </label>
                </div>
                <p className="text-sm text-muted-foreground">{t("packages.coverHelp")}</p>
              </div>
            )}
          </Section>
        </>
      )}
    </EditorSheet>
  );
}

function Section({ title, children, full }: { title: string; children: React.ReactNode; full?: boolean }) {
  return (
    <section className="space-y-4 border-b pb-6 last:border-b-0">
      <h3 className="text-lg font-bold">{title}</h3>
      <div className={full ? "space-y-5" : "grid gap-5 sm:grid-cols-2"}>{children}</div>
    </section>
  );
}
