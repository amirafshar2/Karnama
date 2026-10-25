"use client";

import { CircleHelp } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { useIsMobile } from "@/hooks/use-mobile";
import { api } from "@/lib/api";
import { faN12 } from "@/lib/i18n/fa-n12";
import { useI18n } from "@/lib/i18n/provider";
import { registerTexts, type TextKey } from "@/lib/i18n/registry";
import { useQuery } from "@/lib/query";

registerTexts(faN12);

type Item = { slug: string; title: string; excerpt: string };

/** The "؟" in the header: what this page is for, in two sentences, and the full article behind it. */
export function PageHelp() {
  const { t, dir } = useI18n();
  const isMobile = useIsMobile();
  const path = usePathname() ?? "/";
  const [open, setOpen] = useState(false);
  const key = path.replace(/^\/+|\/+$/g, "") || "dashboard";
  // The most specific page text we have: "attendance/live" → "attendance/live", "assets/label" → "assets".
  const parts = key.split("/");
  let textKey = "help.page.default";
  for (let i = parts.length; i > 0; i--) {
    const k = `help.page.${parts.slice(0, i).join("/")}`;
    if (k in faN12) { textKey = k; break; }
  }
  const article = useQuery({
    queryKey: ["help-page", key],
    enabled: open,
    queryFn: () => api<Item | undefined>(`/api/v1/help/page?key=${encodeURIComponent(key)}`),
  });
  return (
    <>
      <button type="button" onClick={() => setOpen(true)} className="grid size-11 place-items-center rounded-full hover:bg-muted" aria-label={t("help.pageButton")} data-tour="help-button" data-testid="page-help">
        <CircleHelp aria-hidden="true" className="size-5" />
      </button>
      {open && (
        <Sheet open onOpenChange={setOpen}>
          <SheetContent side={isMobile ? "bottom" : dir === "rtl" ? "left" : "right"} className={isMobile ? "rounded-t-2xl" : "w-full sm:max-w-md"}>
            <SheetHeader className="p-5">
              <SheetTitle>{t("help.pageButton")}</SheetTitle>
              <SheetDescription className="text-base leading-8 text-foreground" data-testid="page-help-text">{t(textKey as TextKey)}</SheetDescription>
            </SheetHeader>
            <div className="space-y-3 px-5 pb-6">
              {article.data && (
                <Link href={`/help?a=${encodeURIComponent(article.data.slug)}`} onClick={() => setOpen(false)} className="block rounded-xl border p-4 hover:border-foreground" data-testid="page-help-article">
                  <span className="block text-xs text-muted-foreground">{t("help.pageMore")}</span>
                  <span className="block font-bold">{article.data.title}</span>
                  <span className="mt-1 block text-sm text-muted-foreground">{article.data.excerpt}</span>
                </Link>
              )}
              <Link href="/help" onClick={() => setOpen(false)} className="inline-flex min-h-11 items-center text-sm font-semibold underline underline-offset-4">{t("help.pageAll")}</Link>
            </div>
          </SheetContent>
        </Sheet>
      )}
    </>
  );
}
