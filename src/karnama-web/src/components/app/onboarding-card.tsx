"use client";

import { useQuery } from "@/lib/query";
import { Check, Circle } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faM2Forms } from "@/lib/i18n/fa-m2-forms";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";

registerTexts(faM2Forms);

type Step = { key: string; done: boolean; href: string };
const HIDE_KEY = "karnama.onboarding.hidden";

function readHidden() {
  try {
    return localStorage.getItem(HIDE_KEY) === "1";
  } catch {
    return false;
  }
}

/** Setup assistant on the owner's dashboard (docs/PROMPT2.md 0.2): what's done, what's next, one tap to go there. */
export function OnboardingCard() {
  const { t, formatNumber } = useI18n();
  const [hidden, setHidden] = useState(readHidden);
  const q = useQuery({ queryKey: ["onboarding"], queryFn: () => api<{ steps: Step[]; percent: number; next: Step | null }>("/api/v1/onboarding"), enabled: !hidden });
  if (hidden || !q.data || q.data.percent >= 100) return null;
  const { steps, percent, next } = q.data;
  return (
    <section className="rounded-xl border bg-card p-5 sm:p-6" aria-labelledby="onb-title" data-testid="onboarding">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="onb-title" className="text-lg font-bold">
          {t("onboarding.title")}
        </h2>
        <span className="text-sm text-muted-foreground">{t("onboarding.progress", { percent: formatNumber(percent) })}</span>
      </div>
      <div className="mt-3 h-2 overflow-hidden rounded-full bg-muted" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent} aria-labelledby="onb-title">
        <div className="h-full bg-primary transition-[width]" style={{ width: `${percent}%` }} />
      </div>
      <ol className="mt-4 grid gap-1 sm:grid-cols-2">
        {steps.map((s) => (
          <li key={s.key}>
            <Link href={s.href} className={`flex min-h-11 items-center gap-2 rounded-md px-2 text-sm hover:bg-accent ${s.done ? "text-muted-foreground line-through" : ""}`}>
              {s.done ? <Check className="size-4 text-primary" aria-hidden="true" /> : <Circle className="size-4" aria-hidden="true" />}
              {t(`onboarding.step.${s.key}` as TextKey)}
            </Link>
          </li>
        ))}
      </ol>
      <div className="mt-4 flex flex-wrap gap-2">
        {next && (
          <Button asChild className="h-12 md:h-11">
            <Link href={next.href}>
              {t("onboarding.next")}: {t(`onboarding.step.${next.key}` as TextKey)}
            </Link>
          </Button>
        )}
        <Button
          variant="ghost"
          className="h-12 md:h-11"
          onClick={() => {
            try {
              localStorage.setItem(HIDE_KEY, "1");
            } catch {
              /* private mode */
            }
            setHidden(true);
          }}
        >
          {t("onboarding.hide")}
        </Button>
      </div>
    </section>
  );
}
