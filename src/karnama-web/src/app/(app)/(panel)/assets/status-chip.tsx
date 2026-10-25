"use client";

import { useI18n } from "@/lib/i18n/provider";
import type { TextKey } from "@/lib/i18n/registry";
import { cn } from "@/lib/utils";
import type { AssetItem } from "./types";

export function StatusChip({ a }: { a: Pick<AssetItem, "status" | "assignmentState"> }) {
  const { t } = useI18n();
  const pending = a.assignmentState === "pendingAcceptance" || a.assignmentState === "returnSubmitted";
  const label = pending ? t(`as.state.${a.assignmentState}` as TextKey) : t(`as.status.${a.status}` as TextKey);
  const tone = pending ? "bg-amber-100 text-amber-950 dark:bg-amber-950 dark:text-amber-100"
    : a.status === "available" ? "bg-emerald-100 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-100"
    : a.status === "assigned" ? "bg-sky-100 text-sky-950 dark:bg-sky-950 dark:text-sky-100"
    : "bg-muted text-foreground";
  return <span className={cn("rounded px-1.5 py-0.5 font-medium", tone)}>{label}</span>;
}
