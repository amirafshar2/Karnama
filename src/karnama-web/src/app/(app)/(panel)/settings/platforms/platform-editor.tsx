"use client";

import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { NumberField, TextField, Toggle } from "@/components/app/form";
import { api } from "@/lib/api";
import { faM2Forms } from "@/lib/i18n/fa-m2-forms";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";
import type { Platform } from "./page";

registerTexts(faM2Forms);

export function PlatformEditor({ platform, lang, onClose, onSaved }: { platform: Platform | null; lang: string; onClose: () => void; onSaved: () => void }) {
  const { t } = useI18n();
  const [key, setKey] = useState(platform?.key ?? "");
  const [name, setName] = useState(platform?.nameValues[lang] ?? platform?.name ?? "");
  const [url, setUrl] = useState(platform?.urlTemplate ?? "");
  const [sort, setSort] = useState<number | null>(platform?.sortOrder ?? 100);
  const [active, setActive] = useState(platform?.isActive ?? true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const body = { key, name: { ...(platform?.nameValues ?? {}), [lang]: name }, urlTemplate: url || null, sortOrder: sort ?? 0, isActive: active, version: platform?.version };
      await api(platform ? `/api/v1/platforms/${platform.id}` : "/api/v1/platforms", { method: platform ? "PUT" : "POST", body });
      onSaved();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };
  return (
    <EditorSheet title={platform ? t("common.edit") : t("platforms.new")} onClose={onClose} onSubmit={submit} busy={busy} error={error} testId="platform-editor">
      <TextField label={t("platforms.name")} help={t("platforms.nameHelp")} value={name} onChange={setName} testId="platform-name" />
      <TextField label={t("platforms.key")} help={t("platforms.keyHelp")} value={key} onChange={setKey} dir="ltr" testId="platform-key" />
      <TextField label={t("platforms.url")} help={t("platforms.urlHelp")} value={url} onChange={setUrl} dir="ltr" optional />
      <NumberField label={t("crud.sortOrder")} help={t("crud.sortOrderHelp")} value={sort} onChange={setSort} />
      <Toggle label={t("users.active")} help={t("platforms.activeHelp")} checked={active} onChange={setActive} />
    </EditorSheet>
  );
}
