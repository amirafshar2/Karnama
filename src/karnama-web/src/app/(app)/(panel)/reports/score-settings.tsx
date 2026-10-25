"use client";

import { useState } from "react";
import { useErrorMessage } from "@/components/app/error-text";
import { ErrorText } from "@/components/app/error-text";
import { NumberField, Toggle } from "@/components/app/form";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { notify } from "@/lib/notify";
import { useQuery, useQueryClient } from "@/lib/query";
import type { ScoreSettings, Weights } from "./types";

const KEYS: (keyof Weights)[] = ["quality", "speed", "onTime", "volume", "discipline", "manager", "client"];

/** Weights, minimum samples and who sees the ranking. Every change is audited on the server. */
export function ScoreSettingsView() {
  const { t, formatNumber } = useI18n();
  const qc = useQueryClient();
  const errorMessage = useErrorMessage();
  const current = useQuery({ queryKey: ["score-settings"], gcTime: 0, queryFn: () => api<ScoreSettings>("/api/v1/reports/scores/settings") });
  const [draft, setDraft] = useState<ScoreSettings | null>(null);
  const [busy, setBusy] = useState(false);
  const v = draft ?? current.data;
  if (current.error) return <ErrorText error={current.error} />;
  if (!v) return <div className="h-64 animate-pulse rounded-xl bg-muted" />;
  const sum = KEYS.reduce((a, k) => a + v.weights[k], 0);

  const save = async () => {
    setBusy(true);
    try {
      await api("/api/v1/reports/scores/settings", { method: "PUT", body: v });
      notify.success(t("score.saved"));
      setDraft(null);
      await qc.invalidateQueries({ queryKey: ["scores"] });
      await qc.invalidateQueries({ queryKey: ["score-settings"] });
    } catch (e) {
      notify.error(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="max-w-2xl space-y-6 rounded-xl border bg-card p-5" onSubmit={(e) => { e.preventDefault(); void save(); }} data-testid="score-settings">
      <p className="text-sm text-muted-foreground">{t("score.settingsHelp")}</p>
      <div className="grid gap-4 sm:grid-cols-2">
        {KEYS.map((k) => (
          <div key={k}>
            <NumberField label={t(`score.ind.${k}` as TextKey)} value={v.weights[k]} min={0} max={100}
              onChange={(n) => setDraft({ ...v, weights: { ...v.weights, [k]: n ?? 0 } })} testId={`weight-${k}`} />
            <p className="mt-1 text-xs text-muted-foreground">{sum > 0 ? `${formatNumber(Math.round((v.weights[k] / sum) * 100))}٪` : "—"}</p>
          </div>
        ))}
      </div>
      <NumberField label={t("score.minSamples")} help={t("score.minSamplesHelp")} value={v.minSamples} min={1} max={100} onChange={(n) => setDraft({ ...v, minSamples: n ?? 1 })} testId="min-samples" />
      <Toggle label={t("score.rankingForAll")} help={t("score.rankingForAllHelp")} checked={v.rankingForAll} onChange={(x) => setDraft({ ...v, rankingForAll: x })} testId="ranking-for-all" />
      <Button type="submit" className="h-12 md:h-11" disabled={busy || !draft || sum === 0} data-testid="score-settings-save">
        {t("common.save")}
      </Button>
    </form>
  );
}
