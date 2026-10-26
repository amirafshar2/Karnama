"use client";

import { ChevronDown, Lightbulb, Shuffle } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { useAuth } from "@/lib/auth";
import { faDemoGuide } from "@/lib/i18n/fa-demo-guide";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { cn } from "@/lib/utils";

registerTexts(faDemoGuide);

type Role = "admin" | "editor" | "writer" | "marketer" | "client";
/** The page each step opens (null = the step happens on the dashboard itself). */
const STEPS: Record<Role, (string | null)[]> = {
  admin: ["/orders", "/work", "/attendance/live", "/reports", "/packages"],
  editor: ["/attendance", "/work", "/work", "/chat", "/attendance"],
  writer: ["/attendance", "/work", "/attendance", "/assets", "/reports"],
  marketer: ["/crm", "/crm", "/crm", "/crm", "/chat"],
  client: [null, null, null, null, "/chat"],
};

const isRole = (r: string | null | undefined): r is Role => !!r && r in STEPS;
const storageKey = (role: string) => `karnama.demoGuide.hidden.${role}`;
function storedHidden(role: string) {
  try {
    return typeof window !== "undefined" && localStorage.getItem(storageKey(role)) === "1";
  } catch {
    return false;
  }
}

/**
 * Demo only: what the person in this demo role does at the agency and what to try, step by step, in plain Persian for
 * a visitor who knows nothing about software. Shown at the top of the dashboard; can be folded away (remembered per role).
 */
export function DemoGuide() {
  const { me } = useAuth();
  const { t } = useI18n();
  const role = me?.isDemo ? me.demoRole : null;
  // Folded or not, per role: this visit's choice, else what was remembered on this device.
  const [choice, setChoice] = useState<Record<string, boolean>>({});
  if (!isRole(role)) return null;
  const open = choice[role] ?? !storedHidden(role);

  const toggle = () => {
    try {
      localStorage.setItem(storageKey(role), open ? "1" : "0");
    } catch {
      /* private mode: just this visit */
    }
    setChoice((c) => ({ ...c, [role]: !open }));
  };
  const k = (s: string) => `dg.${role}.${s}` as TextKey;

  return (
    <section className="rounded-3xl border-2 border-[var(--brand-yellow)] bg-card shadow-sm" aria-labelledby="demo-guide-title" data-testid="demo-guide">
      <button type="button" onClick={toggle} aria-expanded={open} className="flex w-full items-start justify-between gap-3 p-5 text-start sm:p-6">
        <span className="min-w-0">
          <span id="demo-guide-title" className="block text-lg font-extrabold sm:text-xl">
            {t("dg.title", { role: t(k("role")) })}
          </span>
          {open && <span className="mt-2 block leading-7 text-muted-foreground">{t(k("who"))}</span>}
        </span>
        <span className="flex shrink-0 items-center gap-1 text-sm font-semibold text-muted-foreground">
          {open ? t("dg.hide") : t("dg.show")}
          <ChevronDown className={cn("size-5 transition-transform", open && "rotate-180")} aria-hidden="true" />
        </span>
      </button>
      {open && (
        <div className="space-y-4 px-5 pb-5 sm:px-6 sm:pb-6">
          <p className="text-sm font-bold">{t("dg.try")}</p>
          <ol className="space-y-2">
            {STEPS[role].map((href, i) => (
              <li key={i} className="flex items-start gap-3 rounded-2xl bg-muted/60 p-3">
                <span className="grid size-8 shrink-0 place-items-center rounded-full bg-[var(--brand-yellow)] text-sm font-bold text-[var(--primary-foreground)]" aria-hidden="true">
                  {(i + 1).toLocaleString("fa-IR")}
                </span>
                <span className="min-w-0 flex-1 leading-7">{t(k(`s${i + 1}`))}</span>
                {href && (
                  <Link href={href} className="shrink-0 self-center rounded-lg px-3 py-2 text-sm font-semibold underline underline-offset-4 hover:bg-background">
                    {t("dg.open")}
                  </Link>
                )}
              </li>
            ))}
          </ol>
          <p className="flex gap-2 rounded-2xl bg-accent/40 p-3 text-sm leading-7">
            <Lightbulb className="mt-1 size-4 shrink-0" aria-hidden="true" />
            <span>{t(k("tip"))}</span>
          </p>
          <p className="flex gap-2 text-sm leading-7 text-muted-foreground">
            <Shuffle className="mt-1 size-4 shrink-0" aria-hidden="true" />
            <span>
              {t("dg.switch")} {t("dg.note")}
            </span>
          </p>
        </div>
      )}
    </section>
  );
}
