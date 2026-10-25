"use client";

import { FlaskConical } from "lucide-react";
import dynamic from "next/dynamic";
import { useAuth } from "@/lib/auth";
import { useI18n } from "@/lib/i18n/provider";

// The dropdown (and its popper code) is only downloaded in demo sessions.
const DemoRoleSwitcher = dynamic(() => import("./demo-role-switcher").then((m) => m.DemoRoleSwitcher), { ssr: false });

/** Shown only in demo sessions: a clear "sample data" bar plus a one-tap role switcher for presentations. */
export function DemoBanner() {
  const { me } = useAuth();
  const { t } = useI18n();
  if (!me?.isDemo) return null;

  return (
    <div className="flex items-center justify-between gap-2 bg-live px-4 py-1.5 text-sm text-live-foreground" role="status">
      <span className="flex items-center gap-2 font-semibold">
        <FlaskConical className="size-4" aria-hidden="true" />
        {t("demo.banner")}
      </span>
      <DemoRoleSwitcher />
    </div>
  );
}
