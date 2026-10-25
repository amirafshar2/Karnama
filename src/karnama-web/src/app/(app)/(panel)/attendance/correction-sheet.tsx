"use client";

import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { JalaliDateField, TextArea, TextField } from "@/components/app/form";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { notify } from "@/lib/notify";

const at = (dayIso: string | null, hhmm: string) => (dayIso && /^\d{1,2}:\d{2}$/.test(hhmm) ? `${dayIso.slice(0, 10)}T${hhmm.padStart(5, "0")}:00+03:30` : null);

export function CorrectionSheet({ onClose }: { onClose: () => void }) {
  const { t } = useI18n();
  const [day, setDay] = useState<string | null>(null);
  const [clockIn, setClockIn] = useState("");
  const [clockOut, setClockOut] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await api("/api/v1/attendance/corrections", { method: "POST", body: { day: day?.slice(0, 10), clockIn: at(day, clockIn), clockOut: at(day, clockOut), reason } });
      notify.success(t("att.correctionSent"));
      onClose();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };
  return (
    <EditorSheet title={t("att.correction")} description={t("att.correctionHelp")} onClose={onClose} onSubmit={submit} busy={busy} error={error} testId="correction">
      <JalaliDateField label={t("att.correctionDay")} help={t("att.correctionDayHelp")} value={day} onChange={setDay} testId="correction-day" />
      <div className="grid grid-cols-2 gap-4">
        <TextField label={t("att.correctionIn")} help={t("att.correctionInHelp")} value={clockIn} onChange={setClockIn} dir="ltr" inputMode="numeric" placeholder="09:00" optional testId="correction-in" />
        <TextField label={t("att.correctionOut")} help={t("att.correctionOutHelp")} value={clockOut} onChange={setClockOut} dir="ltr" inputMode="numeric" placeholder="17:30" optional testId="correction-out" />
      </div>
      <TextArea label={t("att.correctionReason")} help={t("att.correctionReasonHelp")} value={reason} onChange={setReason} testId="correction-reason" />
    </EditorSheet>
  );
}
