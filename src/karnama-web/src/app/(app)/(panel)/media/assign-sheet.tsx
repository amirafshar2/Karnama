"use client";

import { useQuery } from "@/lib/query";
import { useState } from "react";
import { EditorSheet } from "@/components/app/editor-sheet";
import { SelectField } from "@/components/app/form";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import { cn } from "@/lib/utils";
import type { Media, Targets } from "./types";

export function AssignSheet({ media, onClose, onDone }: { media: Media; onClose: () => void; onDone: () => void }) {
  const { t, formatNumber } = useI18n();
  const targets = useQuery({ queryKey: ["media-targets"], queryFn: () => api<Targets>("/api/v1/media/targets") });
  const [workId, setWorkId] = useState(media.workItemId ?? "");
  const [mode, setMode] = useState<"worker" | "client">("worker");
  const [personId, setPersonId] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const work = targets.data?.work.find((w) => w.id === workId);

  const pickWork = (id: string) => {
    setWorkId(id);
    const w = targets.data?.work.find((x) => x.id === id);
    // The work's own assignee is the usual receiver.
    if (w?.assigneeId && !personId) setPersonId(w.assigneeId);
  };

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      await api(`/api/v1/media/${media.id}/assign`, { method: "POST", body: { workItemId: workId || null, toUserId: mode === "worker" ? personId || null : null, toClient: mode === "client" } });
      onDone();
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  return (
    <EditorSheet title={t("media.assignTitle")} description={t("media.assignHelp")} onClose={onClose} onSubmit={submit} busy={busy} error={error} loading={!targets.data && !targets.error} testId="media-assign-sheet" submitLabel={t("media.assign")}>
      {targets.data && (
        <>
          <SelectField
            label={t("media.pickWork")}
            help={t("media.pickWorkHelp")}
            value={workId}
            onChange={pickWork}
            optional
            testId="media-work"
            options={targets.data.work.map((w) => ({ value: w.id, label: `#${formatNumber(w.orderNumber)} ${w.title} — ${w.clientName}` }))}
          />
          <fieldset className="space-y-2">
            <legend className="text-sm font-bold">{t("media.to")}</legend>
            <div className="flex flex-wrap gap-2">
              {(["worker", "client"] as const).map((v) => (
                <label key={v} className={cn("flex min-h-11 cursor-pointer items-center gap-2 rounded-lg border-2 px-3 text-sm", mode === v ? "border-foreground" : "border-border")}>
                  <input type="radio" name="media-to" checked={mode === v} onChange={() => setMode(v)} className="size-4 accent-[var(--foreground)]" data-testid={`media-to-${v}`} />
                  {t(v === "worker" ? "media.toWorker" : "media.toClient")}
                </label>
              ))}
            </div>
          </fieldset>
          {mode === "worker" ? (
            <SelectField
              label={t("media.pickPerson")}
              value={personId}
              onChange={setPersonId}
              testId="media-person"
              options={targets.data.staff.map((s) => ({ value: s.id, label: s.linked ? s.name : `${s.name} ${t("media.notLinked")}` }))}
            />
          ) : (
            work && !work.clientLinked && <p className="rounded-lg bg-amber-100 p-3 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-100">{t("media.clientNotLinked")}</p>
          )}
        </>
      )}
    </EditorSheet>
  );
}
