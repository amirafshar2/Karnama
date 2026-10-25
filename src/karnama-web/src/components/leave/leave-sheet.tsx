"use client";

import { useQuery } from "@/lib/query";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { JalaliDateField, SelectField, TextArea, TextField } from "@/components/app/form";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { KeysetPage } from "@/lib/types";
import { addDays, at, type LeaveType } from "./types";

/** Ask for leave (or, for a manager, register one for a colleague — approved at once). */
export function LeaveSheet({ onClose, onSaved, canRegisterForOthers }: { onClose: () => void; onSaved: () => void; canRegisterForOthers: boolean }) {
  const { t } = useI18n();
  const types = useQuery({ queryKey: ["leave-types"], queryFn: () => api<LeaveType[]>("/api/v1/leave/types") });
  const users = useQuery({ queryKey: ["users", "leave"], enabled: canRegisterForOthers, queryFn: () => api<KeysetPage<{ id: string; displayName: string }>>("/api/v1/users?limit=100") });
  const [typeId, setTypeId] = useState("");
  const [hourly, setHourly] = useState(false);
  const [fromDay, setFromDay] = useState<string | null>(null);
  const [toDay, setToDay] = useState<string | null>(null);
  const [fromTime, setFromTime] = useState("10:00");
  const [toTime, setToTime] = useState("12:00");
  const [reason, setReason] = useState("");
  const [forUser, setForUser] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const type = types.data?.find((x) => x.id === typeId);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      const from = hourly ? at(fromDay, fromTime) : fromDay ? `${fromDay.slice(0, 10)}T00:00:00+03:30` : null;
      const to = hourly ? at(fromDay, toTime) : (toDay ?? fromDay) ? `${addDays((toDay ?? fromDay)!, 1)}T00:00:00+03:30` : null;
      await api("/api/v1/leave/requests", { method: "POST", body: { leaveTypeId: typeId, hourly, from, to, reason, forUserId: forUser || null } });
      onSaved();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  return (
    <EditorSheet title={t("leave.request")} description={t("leave.requestHelp")} onClose={onClose} onSubmit={submit} busy={busy} error={error} testId="leave">
      {canRegisterForOthers && (
        <SelectField label={t("leave.forUser")} help={t("leave.forUserHelp")} value={forUser} onChange={setForUser} optional options={(users.data?.items ?? []).map((u) => ({ value: u.id, label: u.displayName }))} />
      )}
      <SelectField label={t("leave.type")} value={typeId} onChange={setTypeId} optional options={(types.data ?? []).filter((x) => x.isActive).map((x) => ({ value: x.id, label: x.name }))} testId="leave-type" />
      {type?.needsDocument && <p className="text-sm text-amber-700">{t("leave.needsDocument")}</p>}
      <div className="flex gap-2" role="radiogroup">
        {[false, true].map((h) => (
          <label key={String(h)} className="flex min-h-11 flex-1 cursor-pointer items-center justify-center gap-2 rounded-md border px-3 text-sm has-[:checked]:border-foreground has-[:checked]:font-bold">
            <input type="radio" className="sr-only" checked={hourly === h} onChange={() => setHourly(h)} data-testid={h ? "leave-hourly" : "leave-daily"} />
            {h ? t("leave.hourly") : t("leave.daily")}
          </label>
        ))}
      </div>
      <JalaliDateField label={hourly ? t("leave.day") : t("leave.fromDay")} value={fromDay} onChange={setFromDay} testId="leave-from" />
      {hourly ? (
        <div className="grid grid-cols-2 gap-4">
          <TextField label={t("leave.fromTime")} value={fromTime} onChange={setFromTime} dir="ltr" placeholder="10:00" testId="leave-from-time" />
          <TextField label={t("leave.toTime")} value={toTime} onChange={setToTime} dir="ltr" placeholder="12:00" testId="leave-to-time" />
        </div>
      ) : (
        <JalaliDateField label={t("leave.toDay")} value={toDay} onChange={setToDay} optional testId="leave-to" />
      )}
      <TextArea label={t("leave.reason")} help={t("leave.reasonHelp")} value={reason} onChange={setReason} testId="leave-reason" />
    </EditorSheet>
  );
}
