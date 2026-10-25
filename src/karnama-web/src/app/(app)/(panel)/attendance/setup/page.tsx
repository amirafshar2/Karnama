"use client";

import { useQuery } from "@/lib/query";
import { QrCode } from "lucide-react";
import dynamic from "next/dynamic";
import Link from "next/link";
import { useRequire } from "@/components/app/use-require";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { faM4 } from "@/lib/i18n/fa-m4";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts } from "@/lib/i18n/registry";

registerTexts(faM4);

type Setup = import("./setup-forms").Setup;

const SetupForms = dynamic(() => import("./setup-forms").then((m) => m.SetupForms), { ssr: false });

export default function AttendanceSetupPage() {
  const allowed = useRequire([{ code: "attendance.policies.manage", scope: "all" }]);
  const { t } = useI18n();
  const q = useQuery({ queryKey: ["attendance-setup"], enabled: allowed, queryFn: () => api<Setup>("/api/v1/attendance/setup") });
  if (!allowed) return null;
  return (
    <div className="mx-auto max-w-3xl space-y-8">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-extrabold">{t("setup.att.title")}</h1>
          <p className="mt-1 text-muted-foreground">{t("setup.att.subtitle")}</p>
        </div>
        <Button asChild variant="outline" className="h-11">
          <Link href="/attendance/kiosk">
            <QrCode aria-hidden="true" />
            {t("setup.att.kioskLink")}
          </Link>
        </Button>
      </header>
      {q.data ? <SetupForms setup={q.data} /> : <div className="h-64 animate-pulse rounded-xl bg-muted" />}
    </div>
  );
}

