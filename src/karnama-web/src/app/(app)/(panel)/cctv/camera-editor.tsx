"use client";

import { Trash2 } from "lucide-react";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { useErrorMessage } from "@/components/app/error-text";
import { NumberField, TextField, Toggle } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";
import type { Camera } from "./types";

export function CameraEditor({ camera, onClose, onSaved }: { camera: Camera | null; onClose: () => void; onSaved: () => void }) {
  const { t } = useI18n();
  const errorMessage = useErrorMessage();
  const [name, setName] = useState(camera?.name ?? "");
  const [location, setLocation] = useState(camera?.location ?? "");
  const [main, setMain] = useState("");
  const [sub, setSub] = useState("");
  const [order, setOrder] = useState<number | null>(camera?.sortOrder ?? 10);
  const [active, setActive] = useState(camera?.isActive ?? true);
  const [ethics, setEthics] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const save = async () => {
    setBusy(true);
    setError(null);
    try {
      const body = { name, location: location || null, sortOrder: order ?? 10, isActive: active, mainUrl: main || null, subUrl: sub || null, workAreaConfirmed: ethics, version: camera?.version };
      await api(camera ? `/api/v1/cctv/cameras/${camera.id}` : "/api/v1/cctv/cameras", { method: camera ? "PUT" : "POST", body });
      notify.success(t(camera ? "crud.saved" : "crud.created"));
      onSaved();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };
  const remove = async () => {
    if (!camera) return;
    try {
      await api(`/api/v1/cctv/cameras/${camera.id}`, { method: "DELETE" });
      notify.success(t("cctv.deleted"));
      onSaved();
    } catch (e) {
      notify.error(errorMessage(e));
    }
  };
  return (
    <EditorSheet title={camera ? t("cctv.edit") : t("cctv.add")} onClose={onClose} onSubmit={save} busy={busy} error={error} testId="cctv-editor">
      <TextField label={t("cctv.f.name")} help={t("cctv.f.nameHelp")} value={name} onChange={setName} testId="cctv-name" />
      <TextField label={t("cctv.f.location")} help={t("cctv.f.locationHelp")} value={location} onChange={setLocation} optional />
      <TextField label={t("cctv.f.main")} help={camera?.mainMasked ? `${t("cctv.f.mainHelp")} ${t("cctv.f.mainKeep", { masked: camera.mainMasked })}` : t("cctv.f.mainHelp")}
        value={main} onChange={setMain} dir="ltr" autoComplete="off" optional={!!camera} testId="cctv-main" />
      <TextField label={t("cctv.f.sub")} help={t("cctv.f.subHelp")} value={sub} onChange={setSub} dir="ltr" autoComplete="off" optional testId="cctv-sub" />
      <NumberField label={t("cctv.f.order")} help={t("cctv.f.orderHelp")} value={order} onChange={setOrder} min={0} max={1000} />
      {camera && <Toggle label={t("cctv.f.active")} help={t("cctv.f.activeHelp")} checked={active} onChange={setActive} />}
      {!camera && <Toggle label={t("cctv.f.ethics")} help={t("cctv.f.ethicsHelp")} checked={ethics} onChange={setEthics} testId="cctv-ethics" />}
      {camera && <Button type="button" variant="ghost" className="h-11 text-destructive" onClick={remove}><Trash2 aria-hidden="true" />{t("common.delete")}</Button>}
    </EditorSheet>
  );
}
