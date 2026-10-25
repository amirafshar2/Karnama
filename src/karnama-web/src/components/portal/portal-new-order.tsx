"use client";

import { useQuery } from "@/lib/query";
import { useState } from "react";
import { DynamicForm, type FormFieldDef } from "@/components/app/dynamic-form";
import { EditorSheet } from "@/components/app/editor-sheet";
import { SelectField, TextArea, TextField } from "@/components/app/form";
import { api } from "@/lib/api";
import { faM3 } from "@/lib/i18n/fa-m3";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";

registerTexts(faM3);

export function PortalNewOrder({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const { t } = useI18n();
  const services = useQuery({ queryKey: ["services", "options"], queryFn: () => api<{ id: string; name: string }[]>("/api/v1/services/options") });
  const [serviceId, setServiceId] = useState("");
  const form = useQuery({ queryKey: ["service-form", serviceId], enabled: !!serviceId, queryFn: () => api<{ fields: FormFieldDef[] }>(`/api/v1/services/${serviceId}/form`) });
  const [answers, setAnswers] = useState<Record<string, unknown>>({});
  const [title, setTitle] = useState("");
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await api("/api/v1/portal/orders", { method: "POST", body: { clientId: "00000000-0000-0000-0000-000000000000", serviceTypeId: serviceId, title, note, answers, priority: "normal", autoAccept: false } });
      onCreated();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };
  return (
    <EditorSheet title={t("portal.new")} description={t("portal.newHelp")} onClose={onClose} onSubmit={submit} busy={busy} error={error} testId="portal-order">
      <SelectField label={t("orders.service")} help={t("orders.serviceHelp")} value={serviceId} onChange={(v) => { setServiceId(v); setAnswers({}); }} optional options={(services.data ?? []).map((s) => ({ value: s.id, label: s.name }))} testId="portal-service" />
      {form.data && <DynamicForm fields={form.data.fields} value={answers} onChange={setAnswers} fileHelp={t("orders.fileFieldHelp")} />}
      <TextField label={t("orders.titleField")} help={t("orders.titleHelp")} value={title} onChange={setTitle} optional />
      <TextArea label={t("orders.note")} help={t("orders.noteHelp")} value={note} onChange={setNote} optional />
    </EditorSheet>
  );
}
