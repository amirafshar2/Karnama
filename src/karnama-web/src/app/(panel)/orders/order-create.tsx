"use client";

import { useQuery } from "@/lib/query";
import { useState } from "react";
import { DynamicForm, type FormFieldDef } from "@/components/app/dynamic-form";
import { EditorSheet } from "@/components/app/editor-sheet";
import { JalaliDateField, SelectField, TextArea, TextField, Toggle } from "@/components/app/form";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import type { KeysetPage } from "@/lib/types";

export function OrderCreate({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const { t } = useI18n();
  const clients = useQuery({ queryKey: ["clients", "options"], queryFn: () => api<KeysetPage<{ id: string; businessName: string; status: string }>>("/api/v1/clients?limit=100&status=active") });
  const services = useQuery({ queryKey: ["services", "options"], queryFn: () => api<{ id: string; name: string }[]>("/api/v1/services/options") });
  const [clientId, setClientId] = useState("");
  const [serviceId, setServiceId] = useState("");
  const form = useQuery({ queryKey: ["service-form", serviceId], enabled: !!serviceId, queryFn: () => api<{ fields: FormFieldDef[] }>(`/api/v1/services/${serviceId}/form`) });
  const [answers, setAnswers] = useState<Record<string, unknown>>({});
  const [title, setTitle] = useState("");
  const [note, setNote] = useState("");
  const [priority, setPriority] = useState<"normal" | "high" | "urgent">("normal");
  const [deadline, setDeadline] = useState<string | null>(null);
  const [autoAccept, setAutoAccept] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const o = await api<{ id: string }>("/api/v1/orders", { method: "POST", body: { clientId, serviceTypeId: serviceId, title, note, answers, priority, deadline, autoAccept } });
      onCreated(o.id);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  return (
    <EditorSheet title={t("orders.new")} onClose={onClose} onSubmit={submit} busy={busy} error={error} wide testId="order-create">
      <div className="grid gap-5 sm:grid-cols-2">
        <SelectField label={t("orders.client")} help={t("orders.clientHelp")} value={clientId} onChange={setClientId} optional options={(clients.data?.items ?? []).map((c) => ({ value: c.id, label: c.businessName }))} testId="order-client" />
        <SelectField
          label={t("orders.service")}
          help={t("orders.serviceHelp")}
          value={serviceId}
          onChange={(v) => {
            setServiceId(v);
            setAnswers({});
          }}
          optional
          options={(services.data ?? []).map((s) => ({ value: s.id, label: s.name }))}
          testId="order-service"
        />
      </div>
      {form.data && <DynamicForm fields={form.data.fields} value={answers} onChange={setAnswers} fileHelp={t("orders.fileFieldHelp")} />}
      <div className="grid gap-5 sm:grid-cols-2">
        <TextField label={t("orders.titleField")} help={t("orders.titleHelp")} value={title} onChange={setTitle} optional testId="order-title" />
        <SelectField label={t("orders.priority")} value={priority} onChange={setPriority} options={(["normal", "high", "urgent"] as const).map((p) => ({ value: p, label: t(`orders.priority.${p}` as TextKey) }))} />
        <JalaliDateField label={t("orders.deadline")} help={t("orders.deadlineHelp")} value={deadline} onChange={setDeadline} optional />
      </div>
      <TextArea label={t("orders.note")} value={note} onChange={setNote} optional />
      <Toggle label={t("orders.autoAccept")} help={t("orders.autoAcceptHelp")} checked={autoAccept} onChange={setAutoAccept} testId="order-auto" />
    </EditorSheet>
  );
}
