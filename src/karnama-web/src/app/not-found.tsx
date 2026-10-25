"use client";

import Link from "next/link";
import { TilePattern } from "@/components/app/tile-pattern";
import { Button } from "@/components/ui/button";
import { useI18n } from "@/lib/i18n/provider";

export default function NotFound() {
  const { t } = useI18n();
  return (
    <main className="relative grid min-h-dvh place-items-center px-4">
      <TilePattern className="absolute inset-0 opacity-60" id="nf-tile" />
      <div className="relative max-w-sm rounded-xl border bg-card p-8 text-center shadow-sm">
        <h1 className="text-2xl font-extrabold">{t("notFound.title")}</h1>
        <p className="mt-2 text-muted-foreground">{t("notFound.body")}</p>
        <Button asChild className="mt-6 h-12 w-full">
          <Link href="/dashboard">{t("notFound.home")}</Link>
        </Button>
      </div>
    </main>
  );
}
