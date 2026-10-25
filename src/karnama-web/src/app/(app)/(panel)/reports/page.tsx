"use client";

import dynamic from "next/dynamic";
import { useState } from "react";
import { useRequire } from "@/components/app/use-require";
import { useAuth } from "@/lib/auth";
import { faN8 } from "@/lib/i18n/fa-n8";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { hasPermission } from "@/lib/types";
import { cn } from "@/lib/utils";
import { monthRange, PeriodPicker } from "./period";
import { WorkReportView } from "./work-report";

registerTexts(faN8);

const ScoresView = dynamic(() => import("./scores").then((m) => m.ScoresView), { ssr: false });
const ScoreSettingsView = dynamic(() => import("./score-settings").then((m) => m.ScoreSettingsView), { ssr: false });

type Tab = "work" | "scores" | "settings";

/** PROMPT2 §9 + §11: one page for "what did people do in this period" and "how well". */
export default function ReportsPage() {
  const allowed = useRequire([{ code: "reports.view", scope: "own" }, { code: "scores.view", scope: "own" }]);
  const { me } = useAuth();
  const { t } = useI18n();
  const canWork = hasPermission(me, "reports.view", "own") || hasPermission(me, "attendance.self", "own");
  const canScores = hasPermission(me, "scores.view", "own");
  const canConfigure = hasPermission(me, "scores.configure", "all");
  const tabs: Tab[] = [...(canWork ? (["work"] as Tab[]) : []), ...(canScores ? (["scores"] as Tab[]) : []), ...(canConfigure ? (["settings"] as Tab[]) : [])];
  const [picked, setTab] = useState<Tab | null>(null);
  const tab = picked && tabs.includes(picked) ? picked : tabs[0];
  const [[from, to], setRange] = useState<[string | null, string | null]>(() => monthRange(0));
  const range = from && to ? `from=${from.slice(0, 10)}&to=${to.slice(0, 10)}` : null;
  if (!allowed) return null;

  return (
    <div className="space-y-5">
      <header>
        <h1 className="text-2xl font-extrabold">{t("rpt.title")}</h1>
        <p className="mt-1 max-w-prose text-muted-foreground">{t("rpt.subtitle")}</p>
      </header>
      {tabs.length > 1 && (
        <div className="flex flex-wrap gap-2" role="tablist" data-print-hide>
          {tabs.map((x) => (
            <button key={x} role="tab" aria-selected={tab === x} onClick={() => setTab(x)} data-testid={`rpt-tab-${x}`}
              className={cn("min-h-11 rounded-full border-2 px-4 text-sm font-semibold", tab === x ? "border-foreground bg-foreground text-background" : "border-border hover:border-foreground")}>
              {t(`rpt.tab.${x}` as TextKey)}
            </button>
          ))}
        </div>
      )}
      {tab !== "settings" && <PeriodPicker from={from} to={to} onChange={(f, t2) => setRange([f, t2])} />}
      {tab === "work" && range && <WorkReportView range={range} canExport={hasPermission(me, "reports.export", "all")} />}
      {tab === "scores" && range && <ScoresView range={range} />}
      {tab === "settings" && <ScoreSettingsView />}
    </div>
  );
}
