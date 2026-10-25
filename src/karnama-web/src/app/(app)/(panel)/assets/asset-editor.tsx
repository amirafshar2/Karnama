"use client";

import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { JalaliDateField, NumberField, SelectField, TextArea, TextField } from "@/components/app/form";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import { useQuery } from "@/lib/query";
import type { AssetDetail, Category } from "./types";

const day = (iso: string | null) => (iso ? iso.slice(0, 10) : null);
const noon = (d: string | null) => (d ? `${d}T12:00:00+03:30` : null);

export function AssetEditor({ asset, onClose, onSaved }: { asset: AssetDetail | null; onClose: () => void; onSaved: (id: string) => void }) {
  const { t } = useI18n();
  const cats = useQuery({ queryKey: ["asset-categories"], queryFn: () => api<Category[]>("/api/v1/asset-categories") });
  const a = asset?.asset;
  const [name, setName] = useState(a?.name ?? "");
  const [categoryId, setCategoryId] = useState(a?.categoryId ?? "");
  const [brand, setBrand] = useState(a?.brand ?? "");
  const [model, setModel] = useState(a?.model ?? "");
  const [serial, setSerial] = useState(a?.serial ?? "");
  const [tag, setTag] = useState(a?.tag ?? "");
  const [purchasedOn, setPurchasedOn] = useState<string | null>(noon(asset?.purchasedOn ?? null));
  const [price, setPrice] = useState<number | null>(asset?.purchasePrice ?? null);
  const [warranty, setWarranty] = useState<string | null>(noon(a?.warrantyUntil ?? null));
  const [note, setNote] = useState(asset?.note ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const body = {
        name, categoryId, brand: brand || null, model: model || null, serial: serial || null, tag: tag || null,
        purchasedOn: day(purchasedOn), purchasePrice: price, warrantyUntil: day(warranty), note: note || null, version: a?.version,
      };
      const r = a
        ? await api<AssetDetail>(`/api/v1/assets/${a.id}`, { method: "PUT", body })
        : await api<AssetDetail>("/api/v1/assets", { method: "POST", body });
      notify.success(t(a ? "crud.saved" : "crud.created"));
      onSaved(r.asset.id);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  return (
    <EditorSheet title={a ? t("as.edit") : t("as.new")} onClose={onClose} onSubmit={save} busy={busy} error={error} loading={!cats.data && !cats.error} testId="as-editor">
      <TextField label={t("as.name")} help={t("as.nameHelp")} value={name} onChange={setName} testId="as-name" />
      <SelectField label={t("as.category")} value={categoryId} onChange={setCategoryId} testId="as-category"
        options={(cats.data ?? []).filter((c) => c.isActive || c.id === categoryId).map((c) => ({ value: c.id, label: c.name }))} />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField label={t("as.brand")} value={brand} onChange={setBrand} optional dir="auto" />
        <TextField label={t("as.model")} value={model} onChange={setModel} optional dir="auto" />
        <TextField label={t("as.serial")} value={serial} onChange={setSerial} optional dir="ltr" testId="as-serial" />
        <TextField label={t("as.tag")} help={t("as.tagHelp")} value={tag} onChange={setTag} optional dir="ltr" testId="as-tag" />
        <JalaliDateField label={t("as.purchasedOn")} value={purchasedOn} onChange={setPurchasedOn} optional />
        <JalaliDateField label={t("as.warranty")} value={warranty} onChange={setWarranty} optional testId="as-warranty" />
      </div>
      <NumberField label={t("as.price")} value={price} onChange={setPrice} optional min={0} />
      <TextArea label={t("as.note")} value={note} onChange={setNote} optional />
    </EditorSheet>
  );
}
